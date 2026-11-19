using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using wSpot.Models;

namespace wSpot.Services
{
    /// <summary>
    /// The local, persistent store.
    ///
    /// Two tables live in %LocalAppData%\wSpot\wspot.db:
    ///   apps        - the installed-app index, rebuilt from the Start Menu on every start
    ///   usage_stats - how often and how recently each app, file or command was launched
    ///                 (Phase 5). This table is NOT rebuilt: it is the app's memory.
    ///
    /// Search itself doesn't happen here: fuzzy matching can't be expressed as a SQL
    /// query, so GetAllApps() loads everything once and the ViewModel scores it in memory.
    /// </summary>
    public class AppDatabase
    {
        private readonly string _connectionString;

        public AppDatabase()
        {
            var dbPath = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "wSpot", "wspot.db");

            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            _connectionString = $"Data Source={dbPath}";

            EnsureSchema();
        }

        private void EnsureSchema()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using (var createApps = connection.CreateCommand())
            {
                createApps.CommandText =
                    """
                    CREATE TABLE IF NOT EXISTS apps (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        name TEXT NOT NULL,
                        target_path TEXT NOT NULL,
                        arguments TEXT
                    );
                    """;
                createApps.ExecuteNonQuery();
            }

            // usage_key is "kind|identity", lower-cased (see AppEntry.UsageKey). The apps
            // table gets new ids on every start, so usage can't point at an app's id.
            // Dates are stored as ISO-8601 UTC text ("o" format), which sorts correctly as text.
            using (var createUsage = connection.CreateCommand())
            {
                createUsage.CommandText =
                    """
                    CREATE TABLE IF NOT EXISTS usage_stats (
                        usage_key TEXT PRIMARY KEY,
                        kind INTEGER NOT NULL,
                        name TEXT NOT NULL,
                        target_path TEXT NOT NULL,
                        launch_count INTEGER NOT NULL,
                        score REAL NOT NULL,
                        score_updated_utc TEXT NOT NULL,
                        last_launched_utc TEXT NOT NULL
                    );
                    """;
                createUsage.ExecuteNonQuery();
            }
        }

        // ----- apps -----

        public void ReplaceAllApps(List<AppEntry> apps)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            var clear = connection.CreateCommand();
            clear.CommandText = "DELETE FROM apps;";
            clear.ExecuteNonQuery();

            foreach (var app in apps)
            {
                var insert = connection.CreateCommand();
                insert.CommandText =
                    "INSERT INTO apps (name, target_path, arguments) VALUES ($name, $path, $args);";
                insert.Parameters.AddWithValue("$name", app.Name);
                insert.Parameters.AddWithValue("$path", app.TargetPath);
                insert.Parameters.AddWithValue("$args", app.Arguments);
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        public List<AppEntry> GetAllApps()
        {
            var results = new List<AppEntry>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT id, name, target_path, arguments FROM apps ORDER BY name;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new AppEntry
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    TargetPath = reader.GetString(2),
                    Arguments = reader.IsDBNull(3) ? "" : reader.GetString(3)
                });
            }

            return results;
        }

        // ----- usage_stats (Phase 5: frecency) -----

        /// <summary>Loads every usage row, keyed by usage_key. Rows with unreadable dates are skipped.</summary>
        public Dictionary<string, UsageRecord> GetAllUsage()
        {
            var results = new Dictionary<string, UsageRecord>(StringComparer.Ordinal);

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT usage_key, kind, name, target_path, launch_count, score, score_updated_utc, last_launched_utc " +
                "FROM usage_stats;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!TryParseUtc(reader.GetString(6), out DateTime scoreUpdated)) continue;
                if (!TryParseUtc(reader.GetString(7), out DateTime lastLaunched)) continue;

                string key = reader.GetString(0);
                results[key] = new UsageRecord(
                    key,
                    (ResultKind)reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetDouble(5),
                    scoreUpdated,
                    lastLaunched);
            }

            return results;
        }

        /// <summary>Inserts the record, or replaces the existing row with the same usage_key.</summary>
        public void SaveUsage(UsageRecord record)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT OR REPLACE INTO usage_stats
                    (usage_key, kind, name, target_path, launch_count, score, score_updated_utc, last_launched_utc)
                VALUES
                    ($key, $kind, $name, $path, $count, $score, $updated, $last);
                """;
            command.Parameters.AddWithValue("$key", record.Key);
            command.Parameters.AddWithValue("$kind", (int)record.Kind);
            command.Parameters.AddWithValue("$name", record.Name);
            command.Parameters.AddWithValue("$path", record.TargetPath);
            command.Parameters.AddWithValue("$count", record.LaunchCount);
            command.Parameters.AddWithValue("$score", record.Score);
            command.Parameters.AddWithValue("$updated", ToText(record.ScoreUpdatedUtc));
            command.Parameters.AddWithValue("$last", ToText(record.LastLaunchedUtc));
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Deletes rows that haven't been launched since <paramref name="lastLaunchedBeforeUtc"/>.
        /// With a 14-day half-life, anything older than a few months has decayed to
        /// almost nothing anyway, so this just keeps the table from growing forever.
        /// </summary>
        public int PruneUsage(DateTime lastLaunchedBeforeUtc)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM usage_stats WHERE last_launched_utc < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", ToText(lastLaunchedBeforeUtc));
            return command.ExecuteNonQuery();
        }

        /// <summary>Forgets everything the app has learned about what the user opens.</summary>
        public void ClearUsage()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM usage_stats;";
            command.ExecuteNonQuery();
        }

        private static string ToText(DateTime utc) => utc.ToString("o", CultureInfo.InvariantCulture);

        private static bool TryParseUtc(string text, out DateTime value) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }
}

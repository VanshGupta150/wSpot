using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using wSpot.Models;

namespace wSpot.Services{
    public class AppDatabase{
        private readonly string _connectionString;
        public AppDatabase(){
            var dbPath = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "wSpot", "wspot.db");

            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            _connectionString = $"Data Source={dbPath}";
            EnsureSchema();
        }
        private void EnsureSchema(){
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS apps (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    target_path TEXT NOT NULL,
                    arguments TEXT
                );
                """;
            command.ExecuteNonQuery();
        }

        public void ReplaceAllApps(List<AppEntry> apps){
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

        public List<AppEntry> Search(string query)
        {
            var results = new List<AppEntry>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText =
                "SELECT id, name, target_path, arguments FROM apps WHERE name LIKE $pattern ORDER BY name LIMIT 20;";
            command.Parameters.AddWithValue("$pattern", $"%{query}%");

            using var reader = command.ExecuteReader();
            while (reader.Read()){
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
    }
}

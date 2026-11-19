using System;
using System.Globalization;
using System.IO;

namespace wSpot.Services
{
    /// <summary>
    /// Measures how fast searches are, for the project report's latency evaluation.
    ///
    /// Each settled search appends one line to %LocalAppData%\wSpot\search-log.csv:
    ///
    ///   timestamp_utc, query_len, apps_ms, files_ms, engine, results
    ///
    /// apps_ms  = keystroke until the first list (apps and commands) is on its way to the screen
    /// files_ms = keystroke until the list including files is ready. That includes the
    ///            deliberate 120 ms typing pause, so subtract it to get the engine's own time.
    ///
    /// Only the LENGTH of what was typed is written, never the text itself, so the log
    /// is safe to attach to a report. It never throws, and starts over when it passes 5 MB.
    /// </summary>
    public static class SearchLog
    {
        private const long MaxBytes = 5_000_000;
        private const string Header = "timestamp_utc,query_len,apps_ms,files_ms,engine,results";

        private static readonly object Gate = new object();

        public static string LogPath { get; } = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "wSpot", "search-log.csv");

        /// <param name="filesMilliseconds">Null when no file search ran (a one-letter query).</param>
        public static void Record(
            int queryLength,
            double appsMilliseconds,
            double? filesMilliseconds,
            string engine,
            int resultCount)
        {
            try
            {
                string line = string.Join(",",
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    queryLength.ToString(CultureInfo.InvariantCulture),
                    appsMilliseconds.ToString("0.00", CultureInfo.InvariantCulture),
                    filesMilliseconds.HasValue ? filesMilliseconds.Value.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty,
                    engine,
                    resultCount.ToString(CultureInfo.InvariantCulture));

                lock (Gate)
                {
                    string? directory = System.IO.Path.GetDirectoryName(LogPath);
                    if (directory is not null) Directory.CreateDirectory(directory);

                    var info = new FileInfo(LogPath);

                    if (!info.Exists || info.Length > MaxBytes)
                        File.WriteAllText(LogPath, Header + Environment.NewLine + line + Environment.NewLine);
                    else
                        File.AppendAllText(LogPath, line + Environment.NewLine);
                }
            }
            catch (Exception)
            {
                // Measuring must never break searching.
            }
        }
    }
}

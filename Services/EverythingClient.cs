using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace wSpot.Services
{
    /// <summary>How an attempt to use Everything turned out.</summary>
    public enum EverythingStatus
    {
        /// <summary>es.exe ran and answered (the answer may be "no files").</summary>
        Available,

        /// <summary>es.exe could not be found in any of the places we look.</summary>
        NotInstalled,

        /// <summary>es.exe exists, but the Everything app isn't running (es.exe exit code 8).</summary>
        NotRunning,

        /// <summary>es.exe started but failed: unsupported option, timeout, crash...</summary>
        Failed
    }

    public sealed record EverythingResult(EverythingStatus Status, IReadOnlyList<string> FilePaths, IReadOnlyList<string> FolderPaths, string Message)
    {
        public static EverythingResult Failure(EverythingStatus status, string message) =>
            new EverythingResult(status, Array.Empty<string>(), Array.Empty<string>(), message);
    }

    /// <summary>
    /// Finds files by asking Everything (voidtools) through its command-line tool, es.exe.
    ///
    /// Everything keeps an index of every file on every NTFS drive and answers in
    /// milliseconds. We only use it to produce CANDIDATES: files whose names contain
    /// all the words typed. Ranking, merging with apps, and the fallback for when
    /// Everything is missing all live in our own code.
    ///
    /// The command we run (options checked against voidtools' es.exe documentation):
    ///   -n 100                          at most 100 results
    ///   -sort date-modified-descending  most recently changed files first
    ///   /a-d                            files only, no folders
    ///   -export-txt FILE                write the results to a UTF-8 text file
    ///   !path:AppData ...               leave noisy folders out inside Everything itself
    ///   word1 word2 ...                 the search; a space between words means AND
    ///
    /// Why a temp file instead of reading es.exe's console output? Console output uses
    /// the console's code page, which turns non-English file names into "?". The
    /// exported file is UTF-8, so every name survives intact.
    /// </summary>
    public class EverythingClient
    {
        private const int TimeoutMilliseconds = 3000;
        private const int ExitCodeEverythingNotRunning = 8; // es.exe: "Everything IPC window not found"
        private const int MaxTerms = 8;
        private const int MaxTermLength = 64;

        // Folders that would otherwise flood the "most recently modified" candidates
        // (browser caches, package folders...). FileResultFilter does a second,
        // stricter pass on whatever comes back.
        private static readonly string[] ServerSideExclusions =
        {
            "!path:AppData",
            "!path:node_modules",
            "!path:__pycache__"
        };

        private string? _cachedEsPath;

        /// <summary>Full path to es.exe, or null if it can't be found.</summary>
        public string? FindEsExe()
        {
            if (_cachedEsPath is not null && File.Exists(_cachedEsPath))
                return _cachedEsPath;

            _cachedEsPath = null;

            foreach (string candidate in CandidatePaths())
            {
                if (File.Exists(candidate))
                {
                    _cachedEsPath = candidate;
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>The places we look for es.exe, in order.</summary>
        private static IEnumerable<string> CandidatePaths()
        {
            // 1. Next to wSpot.exe (what the README recommends)
            yield return Path.Combine(AppContext.BaseDirectory, "es.exe");

            // 2. Inside an Everything install folder
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Everything", "es.exe");
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Everything", "es.exe");

            // 3. Anywhere on PATH (this is where winget puts it)
            string? pathVariable = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVariable)) yield break;

            foreach (string folder in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string cleaned = folder.Trim().Trim('"');
                if (cleaned.Length > 0)
                    yield return Path.Combine(cleaned, "es.exe");
            }
        }

        /// <summary>
        /// Runs one search. Ordinary problems (es.exe missing, Everything not running,
        /// a timeout) come back as a result with a Status, not as exceptions. The one
        /// exception: OperationCanceledException if the caller cancels, because that
        /// just means the user typed another letter.
        /// </summary>
        public async Task<EverythingResult> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            string? esPath = FindEsExe();
            if (esPath is null)
                return EverythingResult.Failure(EverythingStatus.NotInstalled, "es.exe not found");

            List<string> terms = BuildTerms(query);
            if (terms.Count == 0)
                return new EverythingResult(EverythingStatus.Available, Array.Empty<string>(), Array.Empty<string>(), string.Empty);

            // Run file search and folder search in parallel.
            Task<(EverythingStatus Status, List<string> Paths, string Message)> fileTask =
                RunEsQueryAsync(esPath, terms, maxResults, "/a-d", cancellationToken);
            Task<(EverythingStatus Status, List<string> Paths, string Message)> folderTask =
                RunEsQueryAsync(esPath, terms, maxResults / 4, "/ad", cancellationToken);

            await Task.WhenAll(fileTask, folderTask).ConfigureAwait(false);

            var fileResult = fileTask.Result;
            var folderResult = folderTask.Result;

            // If the file query worked, use it even if the folder query failed.
            if (fileResult.Status == EverythingStatus.Available)
                return new EverythingResult(
                    EverythingStatus.Available,
                    fileResult.Paths,
                    folderResult.Status == EverythingStatus.Available ? folderResult.Paths : Array.Empty<string>(),
                    string.Empty);

            // Both failed — report the file query's error.
            return EverythingResult.Failure(fileResult.Status, fileResult.Message);
        }

        private async Task<(EverythingStatus Status, List<string> Paths, string Message)> RunEsQueryAsync(
            string esPath, List<string> terms, int maxResults, string attributeFlag, CancellationToken cancellationToken)
        {
            string outputFile = Path.Combine(Path.GetTempPath(), "wSpot-es-" + Guid.NewGuid().ToString("N") + ".txt");

            try
            {
                ProcessStartInfo startInfo = CreateStartInfo(esPath, terms, maxResults, attributeFlag, outputFile);
                using var process = new Process { StartInfo = startInfo };

                try
                {
                    process.Start();
                }
                catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
                {
                    return (EverythingStatus.Failed, new List<string>(), "couldn't start es.exe");
                }

                // The results go to the file, but we still drain both pipes so es.exe
                // can never stall on a full pipe buffer.
                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> standardError = process.StandardError.ReadToEndAsync();

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeoutMilliseconds);

                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    TryKill(process);

                    // Cancelled by the caller (user typed again): pass that on.
                    if (cancellationToken.IsCancellationRequested) throw;

                    return (EverythingStatus.Failed, new List<string>(), "es.exe timed out");
                }

                await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);

                int exitCode = process.ExitCode;

                if (exitCode == ExitCodeEverythingNotRunning)
                    return (EverythingStatus.NotRunning, new List<string>(), "Everything isn't running");

                if (exitCode != 0)
                {
                    string detail = FirstLine(standardError.Result);
                    if (detail.Length == 0) detail = FirstLine(standardOutput.Result);
                    if (detail.Length == 0) detail = "exit code " + exitCode;

                    return (EverythingStatus.Failed, new List<string>(), "es.exe failed (" + detail + ")");
                }

                var paths = new List<string>();

                // No results can mean no file at all, so a missing file is not an error.
                if (File.Exists(outputFile))
                {
                    string[] lines = await File.ReadAllLinesAsync(outputFile, Encoding.UTF8, cancellationToken)
                        .ConfigureAwait(false);
                    paths = ParsePaths(lines);
                }

                return (EverythingStatus.Available, paths, string.Empty);
            }
            finally
            {
                TryDelete(outputFile);
            }
        }

        private static ProcessStartInfo CreateStartInfo(string esPath, List<string> terms, int maxResults, string attributeFlag, string outputFile)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = esPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // Each ArgumentList entry becomes exactly one command-line argument;
            // .NET adds any quoting that's needed (for example around a path with spaces).
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add(maxResults.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-sort");
            startInfo.ArgumentList.Add("date-modified-descending");
            startInfo.ArgumentList.Add(attributeFlag);
            startInfo.ArgumentList.Add("-export-txt");
            startInfo.ArgumentList.Add(outputFile);

            foreach (string exclusion in ServerSideExclusions)
                startInfo.ArgumentList.Add(exclusion);

            foreach (string term in terms)
                startInfo.ArgumentList.Add(term);

            return startInfo;
        }

        /// <summary>
        /// Turns what the user typed into safe search words: one argument per word (so
        /// Everything ANDs them), no quote characters, and nothing that starts with "-"
        /// or "/" (so typed text can never be mistaken for an es.exe option).
        /// </summary>
        public static List<string> BuildTerms(string query)
        {
            var terms = new List<string>();

            foreach (string rawTerm in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                string term = rawTerm.Replace("\"", string.Empty).TrimStart('-', '/');
                if (term.Length == 0) continue;

                if (term.Length > MaxTermLength)
                    term = term.Substring(0, MaxTermLength);

                terms.Add(term);
                if (terms.Count == MaxTerms) break;
            }

            return terms;
        }

        /// <summary>
        /// Keeps only lines that look like absolute Windows paths (C:\... or \\server\...).
        /// That also silently drops blank lines, a possible header line, and any stray
        /// message. Duplicates are removed; Everything's order is preserved.
        /// </summary>
        public static List<string> ParsePaths(IEnumerable<string> lines)
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimStart('\uFEFF').Trim(); // \uFEFF = a UTF-8 byte-order mark, if present
                if (!LooksLikeAbsolutePath(line)) continue;

                if (seen.Add(line))
                    paths.Add(line);
            }

            return paths;
        }

        private static bool LooksLikeAbsolutePath(string line)
        {
            if (line.Length < 3) return false;

            bool driveLetterPath = char.IsLetter(line[0]) && line[1] == ':' && (line[2] == '\\' || line[2] == '/');
            bool networkPath = line[0] == '\\' && line[1] == '\\';

            return driveLetterPath || networkPath;
        }

        private static string FirstLine(string text)
        {
            string trimmed = text.Trim();
            int newline = trimmed.IndexOfAny(new[] { '\r', '\n' });
            return newline < 0 ? trimmed : trimmed.Substring(0, newline);
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone, or we're not allowed to stop it: nothing more to do.
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // A leftover temp file is harmless.
            }
        }
    }
}

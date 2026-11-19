using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace wSpot.Services
{
    /// <summary>
    /// The fallback for when Everything isn't available. It walks a few well-known
    /// folders (Desktop, Documents, Downloads) once in the background, keeps the file
    /// paths in memory, and matches typed words against that list.
    ///
    /// Far smaller in scope than Everything (three folders instead of every drive),
    /// but it works on any Windows PC with nothing extra installed, which is what
    /// keeps wSpot usable on a machine we don't control (for example, a demo laptop).
    ///
    /// Matching follows the same rule as Everything: a file matches when its NAME
    /// contains every typed word. Fuzzy ranking happens afterwards, in the ViewModel.
    /// </summary>
    public class FolderScanFileSearcher
    {
        private const int MaxIndexedFiles = 100_000;
        private const int MaxFolderDepth = 8;
        private static readonly TimeSpan RefreshAfter = TimeSpan.FromMinutes(1);

        private static readonly EnumerationOptions ScanOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false, // we recurse ourselves so we can skip noise folders
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };

        private readonly string[] _roots;
        private readonly object _lock = new object();

        private (List<string> Files, List<string> Folders)? _index; // finished file and folder paths
        private Task? _buildTask;              // the scan currently running or last run
        private DateTime _builtAtUtc = DateTime.MinValue;

        public FolderScanFileSearcher(IEnumerable<string>? roots = null)
        {
            _roots = (roots ?? DefaultRoots())
                .Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>Human-readable list of what gets searched, e.g. "Desktop, Documents, Downloads".</summary>
        public string Description =>
            _roots.Length == 0
                ? "no folders"
                : string.Join(", ", _roots.Select(r => Path.GetFileName(r.TrimEnd('\\', '/'))));

        /// <summary>Starts the first scan in the background so it's ready by the first search.</summary>
        public void WarmUp() => EnsureFresh();

        public async Task<(IReadOnlyList<string> Files, IReadOnlyList<string> Folders)> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            EnsureFresh();

            (List<string> Files, List<string> Folders)? snapshot;
            Task? building;

            lock (_lock)
            {
                snapshot = _index;
                building = _buildTask;
            }

            // Very first search: there is nothing to search yet, so wait for the first scan.
            if (snapshot is null && building is not null)
            {
                await building.WaitAsync(cancellationToken).ConfigureAwait(false);

                lock (_lock)
                {
                    snapshot = _index;
                }
            }

            if (snapshot is null) return (Array.Empty<string>(), Array.Empty<string>());

            var index = snapshot.Value;
            return await Task.Run(() =>
            {
                var matchedFiles = Match(index.Files, query, maxResults, cancellationToken);
                var matchedFolders = Match(index.Folders, query, Math.Max(10, maxResults / 4), cancellationToken);
                return ((IReadOnlyList<string>)matchedFiles, (IReadOnlyList<string>)matchedFolders);
            }, cancellationToken).ConfigureAwait(false);
        }

        private static IEnumerable<string> DefaultRoots()
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        /// <summary>
        /// Starts a rescan if there is none yet, or the last one is over a minute old,
        /// so a file you just downloaded shows up. Searches keep using the old list
        /// until the new one is ready.
        /// </summary>
        private void EnsureFresh()
        {
            lock (_lock)
            {
                if (_buildTask is { IsCompleted: false }) return;                               // a scan is already running
                if (_index is not null && DateTime.UtcNow - _builtAtUtc < RefreshAfter) return; // current enough

                _buildTask = Task.Run(() =>
                {
                    var fresh = BuildIndex();

                    lock (_lock)
                    {
                        _index = fresh;
                        _builtAtUtc = DateTime.UtcNow;
                    }
                });
            }
        }

        private (List<string> Files, List<string> Folders) BuildIndex()
        {
            var files = new List<string>();
            var folders = new List<string>();

            foreach (string root in _roots)
            {
                Scan(root, 0, files, folders);
                if (files.Count + folders.Count >= MaxIndexedFiles) break;
            }

            return (files, folders);
        }

        private static void Scan(string directory, int depth, List<string> files, List<string> folders)
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*", ScanOptions))
                {
                    if (FileResultFilter.IsNoise(file)) continue;

                    files.Add(file);
                    if (files.Count + folders.Count >= MaxIndexedFiles) return;
                }

                if (depth >= MaxFolderDepth) return;

                foreach (string subfolder in Directory.EnumerateDirectories(directory, "*", ScanOptions))
                {
                    // Don't even walk into node_modules, .git, bin\Debug and friends.
                    if (FileResultFilter.IsInNoiseFolder(subfolder + "\\")) continue;

                    folders.Add(subfolder);
                    if (files.Count + folders.Count >= MaxIndexedFiles) return;

                    Scan(subfolder, depth + 1, files, folders);
                    if (files.Count + folders.Count >= MaxIndexedFiles) return;
                }
            }
            catch (Exception)
            {
                // A folder we can't read (permissions, vanished, path too long...) is skipped.
            }
        }

        private static List<string> Match(List<string> index, string query, int maxResults, CancellationToken cancellationToken)
        {
            string[] terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0) return new List<string>();

            string compactQuery = string.Concat(terms);
            var matches = new List<(string FullPath, int Score)>();
            int checkedCount = 0;

            foreach (string fullPath in index)
            {
                // Looking at the cancellation flag every few thousand files is cheap enough.
                if ((++checkedCount & 0x0FFF) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                string name = Path.GetFileName(fullPath);
                if (!ContainsAllTerms(name, terms)) continue;

                matches.Add((fullPath, FuzzyMatcher.Score(compactQuery, name) ?? 0));
            }

            return matches
                .OrderByDescending(m => m.Score)
                .Take(maxResults)
                .Select(m => m.FullPath)
                .ToList();
        }

        private static bool ContainsAllTerms(string name, string[] terms)
        {
            foreach (string term in terms)
            {
                if (name.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }

            return true;
        }
    }
}

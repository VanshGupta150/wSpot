using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using wSpot.Helpers;
using wSpot.Models;
using wSpot.Services;

namespace wSpot.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // ----- Tuning knobs: the numbers worth experimenting with for the report -----
        private const int MaxResults = 15;                       // rows in the list
        private const int MinFileQueryLength = 2;                // no file search for a single letter
        private const int AppScoreBonus = 10;                    // base bonus before scaling; apps win ties
        private const int MinFuzzyScore = 5;                     // matches weaker than this are dropped
        private const int FileSearchDebounceMilliseconds = 120;  // wait for a typing pause before asking for files

        // Kept for Week 9 (frecency): that phase writes usage stats back to the
        // database on each launch. Not read anywhere else yet.
        private readonly AppDatabase _database;

        private readonly List<AppEntry> _allApps;
        private readonly FileSearchService _fileSearch = new FileSearchService();

        // Files from the most recent file search (Everything or the fallback).
        private List<AppEntry> _fileCandidates = new List<AppEntry>();

        // Cancelled and replaced on every keystroke, so a slow search for old text
        // can never overwrite the results for the current text.
        private CancellationTokenSource _searchCts = new CancellationTokenSource();

        // True once the user presses Up/Down. From then on, results that arrive late
        // must not steal the highlight back to the first row.
        private bool _userNavigated;

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChanged();
                OnSearchTextChanged();
            }
        }

        public ObservableCollection<AppEntry> Results { get; } = new ObservableCollection<AppEntry>();

        private AppEntry? _selectedResult;
        public AppEntry? SelectedResult
        {
            get => _selectedResult;
            set { _selectedResult = value; OnPropertyChanged(); }
        }

        // One line of small grey text under the list: which engine is searching
        // files when it isn't Everything, or why the last launch failed.
        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage == value) return;
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public RelayCommand LaunchCommand { get; }

        /// <summary>Raised after something launched successfully, so the window can hide itself.</summary>
        public event Action? CloseRequested;

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainViewModel(AppDatabase database)
        {
            _database = database;
            _allApps = _database.GetAllApps(); // loaded once, not re-queried per keystroke
            LaunchCommand = new RelayCommand(_ => LaunchSelected());

            _fileSearch.WarmUp();
        }

        /// <summary>Moves the highlight by one row (-1 = up, +1 = down). Called from the window's key handler.</summary>
        public void MoveSelection(int direction)
        {
            if (Results.Count == 0) return;

            int currentIndex = SelectedResult is null ? -1 : Results.IndexOf(SelectedResult);
            int newIndex = Math.Max(0, Math.Min(Results.Count - 1, currentIndex + direction));

            _userNavigated = true;
            SelectedResult = Results[newIndex];
        }

        private void OnSearchTextChanged()
        {
            // Whatever search was still running for the previous text is now out of date.
            _searchCts.Cancel();
            _searchCts = new CancellationTokenSource();

            _userNavigated = false;

            string query = SearchText.Trim();

            if (query.Length == 0)
            {
                _fileCandidates = new List<AppEntry>();
                StatusMessage = string.Empty;
                ShowResults(query, keepUnmatchedFiles: false);
                return;
            }

            if (query.Length < MinFileQueryLength)
                _fileCandidates = new List<AppEntry>();

            // Step 1, instant: apps, plus any files from the last search that still match.
            ShowResults(query, keepUnmatchedFiles: false);

            // Step 2, in the background: ask for files and merge them in when they arrive.
            if (query.Length >= MinFileQueryLength)
                _ = SearchFilesAsync(query, _searchCts.Token);
        }

        private async Task SearchFilesAsync(string query, CancellationToken token)
        {
            try
            {
                // Debounce: if the user types again during this pause, the token is
                // cancelled and this method ends here without searching anything.
                await Task.Delay(FileSearchDebounceMilliseconds, token);

                FileSearchResult result = await _fileSearch.SearchAsync(query, token);
                token.ThrowIfCancellationRequested();

                // We are back on the UI thread here, so it's safe to touch the list.
                _fileCandidates = result.Files.Select(path => CreateFileEntry(path))
                    .Concat(result.Folders.Select(path => CreateFolderEntry(path)))
                    .ToList();
                StatusMessage = result.Note;
                ShowResults(query, keepUnmatchedFiles: true);
            }
            catch (OperationCanceledException)
            {
                // The user typed something else; a newer search has taken over.
            }
            catch (Exception ex)
            {
                StatusMessage = "File search failed: " + ex.Message;
            }
        }

        /// <summary>
        /// Scores every app and every file candidate against the query, puts them in
        /// one list ordered by score, and shows the top few.
        /// </summary>
        /// <param name="keepUnmatchedFiles">
        /// True for a fresh result set: Everything already decided those files match
        /// (it may have matched on the folder name), so they stay even if the fuzzy
        /// matcher finds nothing in the file name. False when re-checking files left
        /// over from an earlier query: those must still match, or they drop out.
        /// </param>
        private void ShowResults(string query, bool keepUnmatchedFiles)
        {
            var ranked = new List<(AppEntry Entry, int Score)>();

            if (query.Length > 0)
            {
                // Build sets of target paths and app names from the app index for deduplication.
                var appTargets = new HashSet<string>(
                    _allApps.Select(a => a.TargetPath),
                    StringComparer.OrdinalIgnoreCase);

                var appNames = new HashSet<string>(
                    _allApps.Select(a => a.Name),
                    StringComparer.OrdinalIgnoreCase);

                bool searchingUninstaller = query.Contains("uninst", StringComparison.OrdinalIgnoreCase);

                foreach (AppEntry app in _allApps)
                {
                    // Suppress uninstallers from cluttering normal app searches (e.g. "Uninstall Zoom Workplace" for "wor")
                    bool isUninstaller = app.Name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)
                                      || app.Name.EndsWith("Uninstaller", StringComparison.OrdinalIgnoreCase);
                    if (isUninstaller && !searchingUninstaller)
                        continue;

                    int? appScore = FuzzyMatcher.Score(query, app.Name);
                    if (appScore.HasValue && appScore.Value >= MinFuzzyScore)
                    {
                        // Scale the bonus: strong matches get the full bonus, weak ones
                        // get less, so a scattered 6-point match doesn't leapfrog a
                        // solid 15-point file match.
                        int bonus = Math.Min(AppScoreBonus, Math.Max(2, appScore.Value / 3));
                        ranked.Add((app, appScore.Value + bonus));
                    }
                }

                // File names often use "_" or "-" where a person would type a space,
                // so for files the typed spaces are dropped before matching.
                string compactQuery = new string(query.Where(c => !char.IsWhiteSpace(c)).ToArray());

                foreach (AppEntry file in _fileCandidates)
                {
                    // Deduplicate: if an app with the same target or same name is already in the Start Menu index, skip it.
                    if (file.Kind == ResultKind.App && (appTargets.Contains(file.TargetPath) || appNames.Contains(file.Name)))
                        continue;

                    int? fileScore = FuzzyMatcher.Score(compactQuery, file.Name);

                    if (fileScore.HasValue && fileScore.Value >= MinFuzzyScore)
                    {
                        // Depth penalty: shallow paths outrank deeply nested ones.
                        int depth = file.TargetPath.Count(c => c == '\\');
                        int depthPenalty = Math.Max(0, depth - 3); // first 3 levels are free (C:\Users\hp)

                        // Apps found via file search (e.g. Spotify on Desktop) get the app bonus too.
                        int appBonus = file.Kind == ResultKind.App
                            ? Math.Min(AppScoreBonus, Math.Max(2, fileScore.Value / 3))
                            : 0;

                        ranked.Add((file, fileScore.Value + appBonus - depthPenalty));
                    }
                    else if (fileScore.HasValue)
                    {
                        // Matched but below threshold — drop it.
                    }
                    else if (keepUnmatchedFiles)
                        ranked.Add((file, 0));
                }
            }

            List<AppEntry> top = ranked
                .OrderByDescending(r => r.Score) // OrderBy is stable: ties keep apps first, then Everything's order
                .Take(MaxResults)
                .Select(r => r.Entry)
                .ToList();

            ReplaceResults(top);
        }

        private void ReplaceResults(List<AppEntry> newResults)
        {
            // Remember what the user had highlighted, but only if they moved it themselves.
            AppEntry? previous = _userNavigated ? SelectedResult : null;

            Results.Clear(); // the ListBox reports "nothing selected" back to SelectedResult here
            foreach (AppEntry entry in newResults)
                Results.Add(entry);

            AppEntry? toSelect = null;

            if (previous is not null)
            {
                AppEntry wanted = previous;
                toSelect = newResults.FirstOrDefault(r => IsSameResult(r, wanted));
            }

            SelectedResult = toSelect ?? newResults.FirstOrDefault();
        }

        // File entries are rebuilt on every search, so two entries for the same file
        // are different objects. Compare by content, not by reference.
        private static bool IsSameResult(AppEntry a, AppEntry b) =>
            a.Kind == b.Kind
            && string.Equals(a.TargetPath, b.TargetPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

        private static AppEntry CreateFileEntry(string path)
        {
            if (Directory.Exists(path))
                return CreateFolderEntry(path);

            string extension = Path.GetExtension(path);
            bool isLnk = extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
            bool isExe = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);

            if (isLnk)
            {
                string target = path;
                string args = string.Empty;
                try
                {
                    var (resolvedTarget, arguments) = ShortcutResolver.Resolve(path);
                    if (!string.IsNullOrWhiteSpace(resolvedTarget))
                    {
                        target = resolvedTarget;
                        args = arguments;
                    }
                }
                catch
                {
                    // If COM resolution fails, keeping path as TargetPath works via UseShellExecute
                }

                return new AppEntry
                {
                    Kind = ResultKind.App,
                    Name = Path.GetFileNameWithoutExtension(path),
                    TargetPath = target,
                    Arguments = args
                };
            }

            if (isExe)
            {
                return new AppEntry
                {
                    Kind = ResultKind.App,
                    Name = Path.GetFileNameWithoutExtension(path),
                    TargetPath = path
                };
            }

            return new AppEntry
            {
                Kind = ResultKind.File,
                Name = Path.GetFileName(path),
                TargetPath = path
            };
        }

        private static AppEntry CreateFolderEntry(string path) => new AppEntry
        {
            Kind = ResultKind.Folder,
            Name = Path.GetFileName(path.TrimEnd('\\', '/')),
            TargetPath = path
        };

        private void LaunchSelected()
        {
            if (SelectedResult is null) return;

            if (ProcessLauncher.TryLaunch(SelectedResult, out string? error))
            {
                CloseRequested?.Invoke();
            }
            else
            {
                StatusMessage = error ?? "Couldn't open that item.";
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

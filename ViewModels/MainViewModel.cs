using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using wSpot.Helpers;
using wSpot.Models;
using wSpot.Services;

namespace wSpot.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // ----- Tuning knobs: the numbers worth experimenting with for the report -----
        private const int MinFileQueryLength = 2;                // no file search for a single letter
        private const int FileSearchDebounceMilliseconds = 120;  // wait for a typing pause before asking for files
        private const int SuggestionCount = 5;                   // rows shown in the empty box
        private const int ConfirmWindowSeconds = 5;              // how long a second Enter still counts as a confirmation
        private const int UsageRetentionDays = 180;              // usage rows older than this are deleted at startup

        private readonly AppDatabase _database;

        private readonly List<AppEntry> _allApps;
        private readonly List<AppEntry> _commands;
        private readonly FileSearchService _fileSearch = new FileSearchService();

        // Phase 5: what the user has launched, loaded once and kept in step with the database.
        private Dictionary<string, UsageRecord> _usage = new Dictionary<string, UsageRecord>();

        // Files and folders from the most recent file search (Everything or the fallback).
        private List<AppEntry> _fileCandidates = new List<AppEntry>();

        // Cancelled and replaced on every keystroke, so a slow search for old text
        // can never overwrite the results for the current text.
        private CancellationTokenSource _searchCts = new CancellationTokenSource();

        // True once the user presses Up/Down. From then on, results that arrive late
        // must not steal the highlight back to the first row.
        private bool _userNavigated;

        // Latency measurement (Phase 6). Restarted on every keystroke.
        private readonly Stopwatch _queryTimer = new Stopwatch();
        private double _appsMilliseconds;

        // The footer note from the last file search ("es.exe not found..."), kept so
        // it can come back after a "Press Enter again" prompt goes away.
        private string _engineNote = string.Empty;

        // Set after the first Enter on a command that needs confirming.
        private string? _pendingConfirmKey;
        private DateTime _pendingConfirmUntilUtc;

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

        public ObservableCollection<ResultItem> Results { get; } = new ObservableCollection<ResultItem>();

        private ResultItem? _selectedResult;
        public ResultItem? SelectedResult
        {
            get => _selectedResult;
            set { _selectedResult = value; OnPropertyChanged(); }
        }

        // One line of small grey text under the list: which engine is searching
        // files when it isn't Everything, a "Press Enter again" prompt, or why the
        // last launch failed.
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

        // Small grey heading above the list. "Frequently used" while the box is empty.
        private string _listHeader = string.Empty;
        public string ListHeader
        {
            get => _listHeader;
            private set
            {
                if (_listHeader == value) return;
                _listHeader = value;
                OnPropertyChanged();
            }
        }

        public RelayCommand LaunchCommand { get; }

        /// <summary>Ctrl+Enter: open the folder that holds the selected file, folder or app.</summary>
        public RelayCommand RevealCommand { get; }

        /// <summary>
        /// Set by the window: puts text on the clipboard. The ViewModel doesn't call
        /// the Clipboard class itself, which keeps it free of window-specific code.
        /// </summary>
        public Action<string>? CopyToClipboard { get; set; }

        /// <summary>Set by the window: fetches the Windows icon for an app or file (see IconProvider).</summary>
        public Func<AppEntry, Task<ImageSource?>>? IconLoader { get; set; }

        /// <summary>Raised after something launched successfully, so the window can hide itself.</summary>
        public event Action? CloseRequested;

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainViewModel(AppDatabase database)
        {
            _database = database;
            _allApps = _database.GetAllApps(); // loaded once, not re-queried per keystroke

            _commands = QuickCommandCatalog.Create();
            _commands.Add(CreateClearHistoryCommand());

            try
            {
                _database.PruneUsage(DateTime.UtcNow.AddDays(-UsageRetentionDays));
                _usage = _database.GetAllUsage();
            }
            catch (Exception)
            {
                // A damaged stats table must never stop the launcher from starting:
                // it simply starts with no history.
            }

            LaunchCommand = new RelayCommand(_ => LaunchSelected());
            RevealCommand = new RelayCommand(_ => RevealSelected());

            _fileSearch.WarmUp();
        }

        /// <summary>Called by the window just before it appears, so the empty box shows up-to-date suggestions.</summary>
        public void OnWindowShown()
        {
            if (SearchText.Trim().Length == 0)
                ShowSuggestions();
        }

        /// <summary>Moves the highlight by one row (-1 = up, +1 = down). Called from the window's key handler.</summary>
        public void MoveSelection(int direction)
        {
            if (Results.Count == 0) return;

            ClearPendingConfirmation();

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
            ClearPendingConfirmation();

            string query = SearchText.Trim();

            if (query.Length == 0)
            {
                _fileCandidates = new List<AppEntry>();
                _engineNote = string.Empty;
                StatusMessage = string.Empty;
                ShowSuggestions();
                return;
            }

            ListHeader = string.Empty;
            _queryTimer.Restart();

            if (query.Length < MinFileQueryLength)
                _fileCandidates = new List<AppEntry>();

            // Step 1, instant: apps, commands, a calculation, plus any files from the
            // last search that still match.
            ShowResults(query, keepUnmatchedFiles: false);
            _appsMilliseconds = _queryTimer.Elapsed.TotalMilliseconds;

            // Step 2, in the background: ask for files and folders and merge them in when they arrive.
            if (query.Length >= MinFileQueryLength)
                _ = SearchFilesAsync(query, _searchCts.Token);
            else
                LogSearch(query.Length, _appsMilliseconds, null, "none", Results.Count);
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

                _engineNote = result.Note;
                if (_pendingConfirmKey is null)
                    StatusMessage = result.Note;

                ShowResults(query, keepUnmatchedFiles: true);

                LogSearch(query.Length, _appsMilliseconds, _queryTimer.Elapsed.TotalMilliseconds,
                          result.Source.ToString(), Results.Count);
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

        /// <summary>Ranks everything against the query (see ResultRanker) and shows the result.</summary>
        private void ShowResults(string query, bool keepUnmatchedFiles)
        {
            List<RankedResult> ranked = ResultRanker.Rank(
                query, _allApps, _commands, _fileCandidates, keepUnmatchedFiles, _usage, DateTime.UtcNow);

            ReplaceResults(ranked, selectFirst: true);
        }

        /// <summary>
        /// What the empty box shows: the things the user launches most. None of them is
        /// highlighted, so Ctrl+Space followed by a stray Enter launches nothing; Down
        /// picks the first one.
        /// </summary>
        private void ShowSuggestions()
        {
            List<AppEntry> suggestions = ResultRanker.Suggest(
                _allApps, _commands, _usage, DateTime.UtcNow, SuggestionItemExists, SuggestionCount);

            ListHeader = suggestions.Count > 0 ? "Frequently used" : string.Empty;

            List<RankedResult> rows = suggestions.Select(s => new RankedResult(s, 0, null)).ToList();
            ReplaceResults(rows, selectFirst: false);
        }

        private static bool SuggestionItemExists(string path) =>
            path.StartsWith(@"\\", StringComparison.Ordinal) || File.Exists(path) || Directory.Exists(path);

        private void ReplaceResults(List<RankedResult> ranked, bool selectFirst)
        {
            // Remember what the user had highlighted, but only if they moved it themselves.
            ResultItem? previous = _userNavigated ? SelectedResult : null;

            Results.Clear(); // the ListBox reports "nothing selected" back to SelectedResult here

            var items = new List<ResultItem>(ranked.Count);

            foreach (RankedResult row in ranked)
            {
                var item = new ResultItem(row.Entry, row.MatchedIndices);
                items.Add(item);
                Results.Add(item);
            }

            ResultItem? toSelect = null;

            if (previous is not null)
            {
                AppEntry wanted = previous.Entry;
                toSelect = items.FirstOrDefault(i => IsSameResult(i.Entry, wanted));
            }

            SelectedResult = toSelect ?? (selectFirst ? items.FirstOrDefault() : null);

            // Icons arrive a moment later and fill themselves in. Cached ones are set at once.
            foreach (ResultItem item in items)
                _ = LoadIconAsync(item);
        }

        private async Task LoadIconAsync(ResultItem item)
        {
            if (IconLoader is null) return;

            try
            {
                ImageSource? icon = await IconLoader(item.Entry); // resumes on the UI thread
                if (icon is not null)
                    item.SetIcon(icon);
            }
            catch (Exception)
            {
                // An icon is decoration. A row without one still works.
            }
        }

        // Entries are rebuilt on every search, so two entries for the same file are
        // different objects. Compare by content, not by reference.
        private static bool IsSameResult(AppEntry a, AppEntry b) =>
            a.Kind == b.Kind
            && string.Equals(a.TargetPath, b.TargetPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

        private static AppEntry CreateFolderEntry(string path) => new AppEntry
        {
            Kind = ResultKind.Folder,
            Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            TargetPath = path
        };

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

        // ----- launching -----

        private void LaunchSelected()
        {
            if (SelectedResult is null) return;

            AppEntry entry = SelectedResult.Entry;

            // Restart, Shut down, Sign out and Clear history need a second Enter.
            if (entry.RequiresConfirmation && !IsConfirmed(entry))
            {
                ArmConfirmation(entry);
                return;
            }

            string? error = Execute(entry);

            if (error is not null)
            {
                StatusMessage = error;
                return;
            }

            RecordUsage(entry);
            CloseRequested?.Invoke();
        }

        /// <summary>Does whatever the row means. Returns an error message, or null when it worked.</summary>
        private string? Execute(AppEntry entry)
        {
            if (entry.Kind == ResultKind.Calculator)
                return CopyResult(entry.TargetPath);

            if (entry.Run is not null)
            {
                try
                {
                    return entry.Run();
                }
                catch (Exception ex)
                {
                    return $"Couldn't run \"{entry.Name}\": {ex.Message}";
                }
            }

            return ProcessLauncher.TryLaunch(entry, out string? error)
                ? null
                : error ?? "Couldn't open that item.";
        }

        private string? CopyResult(string text)
        {
            if (CopyToClipboard is null)
                return "Copying isn't available.";

            try
            {
                CopyToClipboard(text);
                return null;
            }
            catch (Exception ex)
            {
                return "Couldn't copy to the clipboard: " + ex.Message;
            }
        }

        private void RevealSelected()
        {
            if (SelectedResult is null) return;

            if (ProcessLauncher.TryReveal(SelectedResult.Entry, out string? error))
                CloseRequested?.Invoke();
            else
                StatusMessage = error ?? "Couldn't show that item in its folder.";
        }

        // ----- confirmation for risky commands -----
        // A MessageBox would take focus from the launcher window, which hides itself
        // as soon as it loses focus. So the confirmation is a second Enter, with a
        // prompt in the footer.

        private bool IsConfirmed(AppEntry entry) =>
            _pendingConfirmKey == entry.UsageKey && DateTime.UtcNow <= _pendingConfirmUntilUtc;

        private void ArmConfirmation(AppEntry entry)
        {
            _pendingConfirmKey = entry.UsageKey;
            _pendingConfirmUntilUtc = DateTime.UtcNow.AddSeconds(ConfirmWindowSeconds);
            StatusMessage = "Press Enter again to confirm: " + entry.Name;
        }

        private void ClearPendingConfirmation()
        {
            if (_pendingConfirmKey is null) return;

            _pendingConfirmKey = null;
            StatusMessage = _engineNote;
        }

        // ----- frecency (Phase 5) -----

        private void RecordUsage(AppEntry entry)
        {
            if (!entry.TrackUsage) return;

            _usage.TryGetValue(entry.UsageKey, out UsageRecord? existing);
            UsageRecord updated = FrecencyScorer.RecordLaunch(existing, entry, DateTime.UtcNow);

            _usage[updated.Key] = updated;

            try
            {
                _database.SaveUsage(updated);
            }
            catch (Exception)
            {
                // Losing one launch count isn't worth interrupting the launch with an error.
            }
        }

        // ----- built-in command that needs the database -----

        private AppEntry CreateClearHistoryCommand() => new AppEntry
        {
            Kind = ResultKind.Command,
            Name = "Clear wSpot history",
            Description = "Forget which apps and files you open most",
            Keywords = "reset,forget,usage",
            RequiresConfirmation = true,
            TrackUsage = false,
            Run = ClearUsageHistory
        };

        private string? ClearUsageHistory()
        {
            try
            {
                _database.ClearUsage();
            }
            catch (Exception ex)
            {
                return "Couldn't clear the history: " + ex.Message;
            }

            _usage.Clear();
            return null;
        }

        // ----- latency log (Phase 6) -----

        private static void LogSearch(int queryLength, double appsMilliseconds, double? filesMilliseconds, string engine, int resultCount)
        {
            // On a worker thread, so a slow disk can never slow down typing.
            _ = Task.Run(() => SearchLog.Record(queryLength, appsMilliseconds, filesMilliseconds, engine, resultCount));
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

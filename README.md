# wSpot —
**Phase 1 (Week 4-5: Popup Shell + Global Hotkey)**
**Phase 2 (Week 6-7: App Indexing + Basic Search)**
**Phase 3 (Week 8-9: Custom Fuzzy Matching)**

---

## Phase 1 — Popup Shell + Global Hotkey

A bare-minimum WPF window that appears and disappears on a global
keyboard shortcut. No search, no results — just the shell that
everything else will be built inside.

### What Phase 1 introduced
- `App.xaml` / `App.xaml.cs` — WPF application entry point.
- `MainWindow.xaml` / `MainWindow.xaml.cs` — single search box,
  global hotkey registration (`RegisterHotKey` via P/Invoke),
  show/hide on `Win + Space`.

---

## Phase 2 — App Indexing + Basic Search

Builds on Phase 1. Adds a full app-indexing and search pipeline using
SQLite. Typing in the search box now returns real installed apps via
a `LIKE`-based substring query.

### What's new since Phase 1
- `Models/AppEntry.cs` — plain data class for one indexed app.
- `Services/ShortcutResolver.cs` — resolves `.lnk` files via the
  `IShellLink` COM interface (handles shortcuts with arguments properly,
  unlike naive binary parsing).
- `Services/AppIndexer.cs` — scans the Start Menu folders (both the
  all-users and current-user locations) recursively for `.lnk` files.
- `Services/AppDatabase.cs` — SQLite wrapper (`Microsoft.Data.Sqlite`):
  creates the `apps` table, replaces its contents on each scan, and
  runs substring search queries against it.
- `Services/ProcessLauncher.cs` — thin wrapper around `Process.Start`.
- `Helpers/RelayCommand.cs` — minimal `ICommand` implementation, the
  standard MVVM building block (WPF doesn't ship one).
- `ViewModels/MainViewModel.cs` — first real ViewModel: exposes
  `SearchText`, `Results` (`ObservableCollection<AppEntry>`),
  `SelectedResult`, and `LaunchCommand`.
- `MainWindow.xaml` / `.xaml.cs` — updated to bind to the ViewModel
  instead of handling search in code-behind. A `ListBox` now shows
  results underneath the search box.

---

## Phase 3 — Custom Fuzzy Matching

Builds on Phase 2. Replaces the `LIKE`-based substring search with a
custom fuzzy-matching scorer. Apps are now loaded into memory once
instead of being re-queried from SQLite on every keystroke.

### What's new since Phase 2
- `Services/FuzzyMatcher.cs` — the scorer: subsequence matching (query
  characters must appear in order, not necessarily consecutively) plus
  bonuses for word-start matches, consecutive-character runs, and exact
  case matches, with a small penalty for longer targets.
- `Services/AppDatabase.cs` — `Search(query)` is gone; `GetAllApps()`
  replaces it, returning everything unfiltered.
- `ViewModels/MainViewModel.cs` — loads `_allApps` once in the
  constructor. `RunSearch()` now scores every app in memory with
  `FuzzyMatcher.Score()` and sorts by score instead of hitting the
  database per keystroke.

### What this does differently from Phase 2
- Typing `vsc` now finds "Visual Studio Code" even though those letters
  aren't consecutive — Phase 2's substring match required typing a
  literal chunk of the real name.
- Results are ranked by match quality, not alphabetically within a
  `LIKE` filter.

### Known simplifications (intentional, flagged for later)
- **Greedy matching, not globally optimal.** The scorer takes the first
  available match for each query character rather than searching every
  possible alignment. A fully optimal version needs dynamic programming —
  deliberate scope decision, not an oversight.
- **No typo tolerance yet** (Levenshtein distance) — flagged as
  optional/if-time-allows in the blueprint, not core to this phase.
- **`_database` field in `MainViewModel` is currently unused** after the
  constructor — kept deliberately for Week 9, which will write usage
  stats on each launch.

---

## Next step (Construction Week 10)
Integrate Everything's CLI for file search, merging its results into
the same scored-and-ranked list `FuzzyMatcher` already produces for
apps — tag each result with its type (app vs. file) so the UI can show
different icons once the polish pass adds them.

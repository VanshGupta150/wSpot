# wSpot — Phase 4 (Construction Week 8: File Search)

Builds on Phase 3. Until now wSpot only found installed apps. This phase adds
**file search** and merges files and apps into one ranked list. If Everything
isn't installed, it falls back to scanning a few folders, so wSpot still works
on any Windows PC (for example, the laptop you demo on).

> **Status, please read.** This phase was written in an environment with no .NET
> SDK, so it has **not been compiled or run**. It was checked for syntax errors and
> for mismatches between files (every XAML handler and binding has a matching C#
> member, every cross-file call resolves). Build it in Visual Studio
> (Build → Rebuild Solution). If the Error List shows anything, send it back and
> it gets fixed.

## What you'll see

- Type `report` and apps and files appear in one list. Each row has the name, a
  grey second line (the folder for a file, "Application" for an app) and a small
  **App** / **File** badge on the right.
- With Everything running, files come from every drive in a few milliseconds.
  Without it, a grey note under the list says what is being searched instead.
- Enter or a click opens the item and the window hides immediately. If a file was
  moved or deleted meanwhile, the note under the list says so instead of crashing.
- Hover a row to see the full path.

## How a search works now

1. **Every keystroke** cancels whatever file search was still running.
2. **Instantly:** apps are scored with `FuzzyMatcher` (plus a +10 bonus so an app
   beats an equally good file), together with any files from the previous search
   that still match. The list updates at once.
3. **After a 120 ms typing pause** (and at least 2 characters), `FileSearchService`
   asks Everything for up to 100 candidate files whose *name contains every typed
   word*, newest first, with noisy folders excluded.
4. Candidates are re-scored with `FuzzyMatcher`, merged with the apps, and the top
   15 are shown. If you had moved the highlight with Up/Down, it stays on your row
   when late results arrive instead of jumping back to the top.
5. If Everything isn't usable, `FolderScanFileSearcher` answers instead, from
   Desktop, Documents and Downloads, and the note appears.

## Set up Everything (optional, recommended)

1. **Install Everything** from voidtools.com (or `winget install voidtools.Everything`)
   and keep it running (it sits in the tray).
2. **Get es.exe**, voidtools' command-line tool: the "ES" download on voidtools.com
   (a zip containing es.exe), or `winget install --id voidtools.Everything.Cli`.
3. **Let wSpot find es.exe**, any one of these is enough:
   - next to `wSpot.exe` (for F5 runs: `bin\Debug\net8.0-windows\`)
   - next to `wSpot.csproj`, where the project file copies it on every build
   - in a folder on your PATH (winget does this). Restart Visual Studio afterwards so
     it picks up the new PATH.
4. **Check it works.** In Command Prompt run `es -n 5 .docx` (or give the full path to
   es.exe). You should get five file paths. "Everything IPC window not found" means
   Everything isn't running.

Use the stable Everything 1.4.x. The 1.5 alpha uses a separate instance name that
es.exe doesn't connect to by default.

## What the note under the list means

| Note | Meaning |
|---|---|
| *(no note)* | Everything answered |
| `es.exe not found — searching Desktop, Documents, Downloads only` | step 2 or 3 above isn't done |
| `Everything isn't running — searching … only` | es.exe is found, but the Everything app isn't running |
| `es.exe failed (…) — searching … only` | usually an es.exe older than 1.1.0.x (it doesn't know `-export-txt`); update it |
| `es.exe timed out — searching … only` | Everything took over 3 seconds; it is retried after 5 seconds |

## Files in this phase

**New**

| File | What it does |
|---|---|
| `Services/EverythingClient.cs` | Runs es.exe, reads the results, maps exit code 8 to "Everything isn't running" |
| `Services/FolderScanFileSearcher.cs` | The fallback: background scan of a few folders, kept in memory, rescanned at most once a minute |
| `Services/FileSearchService.cs` | The one door the app uses: tries Everything, else the fallback; remembers a failure for 5 s |
| `Services/FileResultFilter.cs` | Noise list: Windows, Program Files, AppData, node_modules, .git, bin\Debug... |

**Changed**

| File | Change |
|---|---|
| `Models/AppEntry.cs` | Added `Kind` (App or File), `Subtitle` and `KindLabel`. The class keeps its name so earlier phases still merge |
| `Services/ProcessLauncher.cs` | `Launch` became `TryLaunch(entry, out error)`, which never throws |
| `ViewModels/MainViewModel.cs` | Debounced, cancellable file search merged with app results; `StatusMessage`; `MoveSelection` moved here from the window |
| `MainWindow.xaml` | Two-line rows with a badge, a dark selection highlight, and the footer note |
| `MainWindow.xaml.cs` | Up/Down call `_viewModel.MoveSelection`; the window hides when something launched |
| `wSpot.csproj` | Optional line that copies `es.exe` to the output folder if you drop it next to the project |

**Unchanged from Phase 3:** `App.xaml`, `App.xaml.cs`, `Helpers/RelayCommand.cs`,
`Services/AppDatabase.cs`, `Services/AppIndexer.cs`, `Services/ShortcutResolver.cs`,
`Services/FuzzyMatcher.cs`.

## Merging this into your existing project

1. Close Visual Studio.
2. Open your project folder (the one that contains `wSpot.csproj`) and copy everything
   from this folder over it, keeping the subfolders (`Models`, `Services`,
   `ViewModels`, `Helpers`). Say yes to "replace". Do **not** touch `bin`, `obj`,
   `.vs` or `.git`.
3. Reopen the solution. You don't need "Add Existing Item": a project like this picks up
   new `.cs` files in its folder automatically.
4. Build → Rebuild Solution, then press F5.

Coming from Phase 2? Same steps. This folder already contains Phase 3's changes
(`FuzzyMatcher.cs` is new since Phase 2, and `AppDatabase.cs` lost `Search()` in favour
of `GetAllApps()`), so copying everything brings you fully up to date.

Make sure `wSpot.csproj` still has the `Microsoft.Data.Sqlite` package reference. The one
in this folder does.

## Quick test checklist

1. Ctrl+Space, type `calc`. Calculator comes first, with an **App** badge.
2. Type part of a document name you know (say `resume`). File rows appear with a folder path.
3. Up/Down moves the highlight, Enter opens the file in its normal program, and the window hides.
4. Click a row. It opens too.
5. Type `xyzzy123`. Nothing found, no crash.
6. Quit Everything from the tray, then search a file in Documents. It is still found, and
   the note appears.
7. Find a file, delete it (leave wSpot open), press Enter. The note says it no longer exists.
8. Type an app name that is also a file name. The app is above the file.
9. Mash keys quickly. No flicker or crash, and the list settles on what you typed last.

## Design decisions worth writing in the report

- **Everything only produces candidates.** Matching, ranking, merging with apps, debouncing,
  cancellation and the fallback are our own code, so the project doesn't depend on someone
  else's engine for its core logic (the course's "engine availability" criterion).
- **Substring first, fuzzy second.** Everything finds files whose name contains all typed
  words, which keeps the candidate list precise. `FuzzyMatcher` then re-orders them.
- **Debounce plus cancellation.** Without them, a slow search for "rep" can finish after the
  search for "report" and overwrite it with stale results.
- **Results go through a UTF-8 temp file, not console output.** Console output uses the
  console's code page and turns non-English file names into "?".
- **A stable highlight.** Late results never pull the highlight away from a row you chose.
- **A graceful fallback.** No Everything means a smaller scope, not a broken feature, and
  the app says so.

## Known limits (intentional, flagged for later)

- Everything matches words, not abbreviations: typing `qrpt` won't find
  "Quarterly Report" (apps still support abbreviations). A wildcard query such as
  `*q*r*p*t*` could add this later.
- Files only, not folders.
- The fallback covers 3 folders, at most 100,000 files, 8 levels deep.
- No icons yet (Week 10).
- The class is still called `AppEntry` even though it now holds files too.
- The noise lists are fixed in code. Making them configurable is a Week 10 candidate.
- No automated tests yet. `EverythingClient.BuildTerms`, `EverythingClient.ParsePaths`
  and `FileResultFilter.IsNoise` are static and easy to unit-test for the CI setup.
- There is no `.gitignore` in this folder. If you use Git, let Visual Studio add its
  standard one (it ignores `bin/`, `obj/`, `.vs/`).

## Next step (Construction Week 9)

Frecency: a `usage_stats` table in SQLite (kind, path, launch count, last launched),
written from `MainViewModel.LaunchSelected`, with a decaying score added to the ranking
so things you open often rise to the top. Files and apps both use the same table.

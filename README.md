# wSpot — 
Phase 1 (Week 4: popup shell + global hotkey)
Phase 2 (Week 6: App Indexing + Basic Search)

## What's new since Phase 1
`Models/AppEntry.cs` — plain data class for one indexed app.
`Services/ShortcutResolver.cs` — resolves `.lnk` files via the
`IShellLink` COM interface (handles shortcuts with arguments properly, unlike naive binary parsing).
`Services/AppIndexer.cs` — scans the Start Menu folders (both the all-users and current-user locations) recursively for `.lnk` files.
`Services/AppDatabase.cs` — SQLite wrapper (`Microsoft.Data.Sqlite`): creates the `apps` table, replaces its contents on each scan, and runs substring search queries against it.
`Services/ProcessLauncher.cs` — thin wrapper around `Process.Start`.
`Helpers/RelayCommand.cs` — minimal `ICommand` implementation, the standard building block for MVVM commands (WPF doesn't ship one).
`ViewModels/MainViewModel.cs` — the first real ViewModel: exposes
`SearchText`, `Results` (an `ObservableCollection<AppEntry>`), `SelectedResult`, and `LaunchCommand`.
`MainWindow.xaml` / `.xaml.cs` — updated to bind to the ViewModel instead of handling search directly in code-behind. A `ListBox` now shows results underneath the search box.

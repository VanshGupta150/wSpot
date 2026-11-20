# wSpot — Spotlight Launcher for Windows

[![CI](https://github.com/VanshGupta150/wSpot/actions/workflows/ci.yml/badge.svg)](https://github.com/VanshGupta150/wSpot/actions/workflows/ci.yml)
[![Release](https://img.shields.io/badge/release-v1.0.0-blue.svg)](https://github.com/VanshGupta150/wSpot/releases)
[![Tests](https://img.shields.io/badge/tests-72%20passed-brightgreen.svg)](tests/wSpot.Tests)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078d4.svg)]()
[![Framework](https://img.shields.io/badge/.NET-8.0-purple.svg)]()

`wSpot` is a fast, keyboard-first desktop launcher for Windows inspired by macOS Spotlight. Built with WPF and .NET 8, it provides sub-millisecond search across installed applications, nested files, live calculations, and system controls—all from a single universal hotkey.

---

## Features

### 🚀 Instant Access & Minimalist UI
* **Global Hotkey:** Press `Ctrl + Space` from any application to summon the centered search bar.
* **Dismiss on Focus Loss:** Seamlessly hides on `Esc` or clicking anywhere outside the window.
* **Graceful Conflict Handling:** Alerts and cleanly exits if another process already uses `Ctrl + Space`, preventing ghost background tasks.

### 🔍 Intelligent Search & Ranking
* **Subsequence Fuzzy Matcher:** Type abbreviations like `gc` to instantly find **Google Chrome** with word-boundary bonus scoring.
* **Self-Learning Frecency:** Combines frequency and recency using mathematical exponential decay (14-day half-life). Items you launch often naturally rise to the top.
* **Frequently Used Suggestions:** Pressing `Ctrl + Space` with an empty search box displays your top used apps and files.
* **Match Highlighting:** Real-time visual feedback rendering matched query letters in bold accent color.

### 📁 Deep File & Folder Search
* **Dual Search Engine:** Integrates asynchronously with the Voidtools *Everything* IPC engine (`es.exe`) for sub-second NTFS file discovery, with an automatic directory crawler fallback.
* **Reveal in Explorer:** Press `Ctrl + Enter` on any file, folder, or application to immediately open File Explorer with the item selected.

### 🧮 Live Calculator & Quick Utilities
* **Math Parser:** Type any math expression (`2+3*4`, `sqrt(144) + 5`, `2^10`, `sin(pi/2)`) to view the answer at the top row. Press `Enter` to copy the result directly to your clipboard.
* **Quick System Commands:** Instant access to `Lock`, `Sleep`, `Task Manager`, `Device Manager`, `Settings`, `File Explorer`, and `Snipping Tool`.
* **Two-Step Confirmation:** Destructive commands (`Restart`, `Shut down`, `Sign out`, `Clear history`) require a second `Enter` within 5 seconds to prevent accidental actions.
* **Web Fallback:** Seamlessly opens queries in your default browser if no local matches are found.

---

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl + Space` | Open or toggle the wSpot search bar |
| `Esc` | Clear search text and hide the window |
| `Up / Down` | Navigate up and down through the result list |
| `Enter` | Launch selected app/file, copy calculation, or execute command |
| `Ctrl + Enter` | Open containing folder and select the item in File Explorer |

---

## Installation & Running

### Option 1: Standalone Portable Executable (Recommended)
No installation or .NET SDK required.
1. Download `wSpot-v1.0.0-win-x64.zip` from [Releases](https://github.com/VanshGupta150/wSpot/releases).
2. Extract the archive.
3. Double-click `wSpot.exe` to start.
4. Press `Ctrl + Space` anytime to summon the launcher.

*(Optional: Install [Voidtools Everything](https://www.voidtools.com/) for ultra-fast NTFS file indexing).*

### Option 2: Build From Source
Prerequisites: Windows 10/11 and [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
# Clone the repository
git clone https://github.com/VanshGupta150/wSpot.git
cd wSpot

# Build solution
dotnet build wSpot.slnx -c Release

# Run the app
dotnet run --project wSpot.csproj -c Release
```

---

## Testing & Quality Assurance

The project includes an automated test suite containing **72 unit tests** built with xUnit, running continuously via GitHub Actions CI:

```powershell
# Run the test suite
dotnet test wSpot.slnx
```

**Test Coverage Areas:**
* `FrecencyScorer`: Exponential decay, half-life math, score boost clamping.
* `ExpressionEvaluator`: Arithmetic precedence, power associativity (`2^3^2 = 512`), function evaluations, and silent NaN/infinity rejection.
* `ResultRanker`: Multi-tier scoring, calculator row precedence, web search fallback, and suggestion filtering.
* `FuzzyMatcher`: Subsequence detection, word-boundary scoring, and character index highlighting.
* `EverythingClient`: CLI argument construction and path deduplication.
* `FileResultFilter`: Noise suppression for temp files, caches, and system directories.

---

## Architecture & Tech Stack

```
wSpot (WPF UI - MVVM)
 ├── View:        MainWindow.xaml (XAML layout, dark theme, match highlighting)
 ├── ViewModel:   MainViewModel.cs (Reactive data-binding, cancellation tokens, async search)
 ├── Services:    FuzzyMatcher, ResultRanker, FrecencyScorer, EverythingClient, ProcessLauncher
 └── Database:    AppDatabase.cs (Local SQLite database in %LocalAppData%\wSpot\wspot.db)
```

* **Frontend:** C# / WPF, XAML, P/Invoke (`user32.dll`), COM Interop (`IShellLink`).
* **Backend Core:** .NET 8.0 Windows Desktop SDK.
* **Storage:** Embedded SQLite (`Microsoft.Data.Sqlite`) for app catalog and frecency memory.
* **CI/CD:** GitHub Actions running on `windows-latest`.

---

## License

Developed as a Semester 5 Software Engineering Project.

# wSpot — Phases 5 and 6 (Construction Weeks 9 and 10)

**Phase 5: frecency.** wSpot learns what you open and puts it first.
**Phase 6: utility and polish.** A calculator, system commands, a web-search fallback, real
icons, highlighted matches, "show in folder", and a latency log for your evaluation.

---

## How the list is ordered now

Every row gets a score. The list is sorted by it, best first.

```
score = how well the letters match (FuzzyMatcher)
      + 2..10 scaled bonus if it's an app or command
      + 0..45 frecency boost: how much you use it   <- Phase 5
      - depth penalty (shallow paths outrank deeply nested ones)
```

Two rows sit outside the scoring: a valid **calculation** is always first, and the
**"Search the web" row** is always last. Up to 15 scored rows are shown between them.

---

# Phase 5 — Frecency (Week 9)

## What you'll see

- Open Notepad a few times through wSpot. Type `n` and Notepad is now at the top,
  even if another app matches `n` slightly better.
- Press **Ctrl+Space** with the box empty. A **Frequently used** list shows up to five
  things you open most. Nothing is highlighted yet, so a stray Enter launches nothing;
  press Down to pick one, then Enter.
- It survives restarts, and it covers apps, files, folders and commands.
- The command **Clear wSpot history** (type `clear`, Enter, then Enter again to confirm)
  makes wSpot forget everything.

## How it works

"Frecency" means frequency and recency in one number.

- Every item has a **score**. Launching it adds **1**.
- The score **halves every 14 days** (`HalfLifeDays` in `FrecencyScorer.cs`).
- So something opened five times today beats something opened five times last month, and
  something opened daily beats something opened once.

Only two numbers are stored per item: the score and the moment it was last updated. That is
enough because the decay is exponential: decaying "from then until now" in one step gives the
same answer as decaying a bit every day. No launch history needs to be kept, which makes the
storage cost per item constant.

The score becomes a ranking boost with a logarithm, capped at 45:

```
boost = min(45, round(12 * ln(1 + score)))
```

| What you did | Score | Boost |
|---|---|---|
| launched once | 1 | +8 |
| launched 3 times today | 3 | +17 |
| launched 10 times recently | 10 | +29 |
| launched once a day for a week | 6.1 | +23 |
| launched once a day for a month | 16 | +34 |
| launched once a day, forever | 20.7 | +37 |
| 3 launches, then nothing for 14 / 28 / 56 days | 1.5 / 0.75 / 0.19 | +11 / +7 / +2 |

All of these numbers (`HalfLifeDays`, `BoostScale`, `MaxBoost`, `MinSuggestionScore`) sit at the
top of `FrecencyScorer.cs`, so you can experiment for the report.

## Where it's stored

A SQLite table in `%LocalAppData%\wSpot\wspot.db`, called `usage_stats`:

```sql
CREATE TABLE usage_stats (
    usage_key          TEXT PRIMARY KEY,  -- "kind|target|name", lower-cased
    kind               INTEGER NOT NULL,  -- 0=App, 1=File, 2=Folder, 3=Command...
    name               TEXT NOT NULL,
    target_path        TEXT NOT NULL,
    launch_count       INTEGER NOT NULL,  -- lifetime total, never decays
    score              REAL NOT NULL,     -- decayed launch counter
    score_updated_utc  TEXT NOT NULL,     -- when score was last recomputed
    last_launched_utc  TEXT NOT NULL
);
```

Rows older than 180 days with no launch are deleted at startup so the file stays small.

---

# Phase 6 — Utility and Polish (Week 10)

## 1. Calculator

Type any maths and the top row shows the answer:

- `2+3*4` -> `14`
- `(10 - 2) * 5` -> `40`
- `2^10` or `2**10` -> `1024`
- `sqrt(144) + 5` -> `17`
- `15% of 80` -> type `80 * 0.15` -> `12`
- `sin(pi/2)` -> `1`

Press **Enter** to copy the answer to the clipboard (the window closes, paste it wherever you
wanted it).

It uses a small recursive-descent parser (`ExpressionEvaluator.cs`) that only accepts
numbers, arithmetic and safe functions (`sqrt`, `cbrt`, `abs`, `sin`, `cos`, `tan`, `asin`,
`acos`, `atan`, `ln`, `log`, `log2`, `exp`, `floor`, `ceil`, `round`, `pow`, `min`, `max`) and
constants (`pi`, `e`, `tau`). Division by zero, `sqrt(-1)` and overflows fail silently, so
invalid maths never turns into a calculator row.

Plain numbers like `42` or words like `vs2022` never trigger the calculator: it requires at
least one operator or function call.

## 2. Quick system commands

Type:

- **Lock** -> locks the PC (`rundll32 user32.dll,LockWorkStation`).
- **Sleep** -> puts the PC to sleep via the Windows power API (`SetSuspendState`).
- **Restart**, **Shut down**, **Sign out** -> asks for a second Enter to confirm:
  press Enter once, the footer says `Press Enter again to confirm: Restart`, press Enter within
  5 seconds and the command runs. Any other key cancels.
- **Task Manager**, **Device Manager**, **Services**, **Control Panel**, **Settings**,
  **Windows Update** -> opens the corresponding Windows management tool.
- **File Explorer**, **Downloads**, **Recycle Bin** -> opens those locations.
- **Snipping Tool** -> triggers the Windows screen snip overlay.

Keywords work: `reboot` finds Restart, `processes` finds Task Manager, `trash` finds Recycle Bin.

## 3. Web-search fallback

The last row is always **Search the web for "..."**. Press Enter to open the query in your
default browser. When nothing on your PC matches, this is the only row, so typing a question
and pressing Enter is seamless.

## 4. Real Windows icons

Each row loads the icon Windows Explorer shows for that item (`IconProvider.cs`):

- `.exe`, `.lnk` and `.ico` files read their own individual icon.
- Every other file type (`.pdf`, `.docx`, `.png`...) shares one icon per extension, cached
  in memory.
- Folders show the standard Windows folder icon.
- Built-in commands show a symbol from the Segoe MDL2 Assets font that ships with Windows.
- Icons load in the background, so the list appears immediately and icons fill in a fraction of
  a second later.

## 5. Match highlighting

The letters that matched what you typed are drawn in **bold** and in an accent colour, using
the `HighlightText` attached property (`HighlightText.cs`). Neighbouring letters are grouped
into one `Run` so long names render smoothly.

## 6. Show in folder

Press **Ctrl+Enter** on any file, folder or app to open Explorer with that item selected.

## 7. Latency evaluation log

Every search you settle on appends a row to:

```
%LocalAppData%\wSpot\search-log.csv
```

Format:

```csv
timestamp_utc,query_len,apps_ms,files_ms,engine,results
2026-11-19T10:45:01.1234567Z,3,1.42,12.80,Everything,15
```

- `apps_ms`: milliseconds from keystroke until apps and commands were ready.
- `files_ms`: milliseconds from keystroke until files were merged in.
- `engine`: `Everything`, `FolderScan` or `none` (for 1-letter searches).
- `results`: how many rows were shown.

To compute **p50 and p95 latency** for your report, run this PowerShell snippet:

```powershell
$log = Import-Csv "$env:LOCALAPPDATA\wSpot\search-log.csv"
$appTimes = $log | ForEach-Object { [double]$_.apps_ms } | Sort-Object
$p50 = $appTimes[[math]::Floor($appTimes.Count * 0.50)]
$p95 = $appTimes[[math]::Floor($appTimes.Count * 0.95)]
Write-Host "Apps latency: p50 = $p50 ms, p95 = $p95 ms (sample size: $($appTimes.Count))"
```

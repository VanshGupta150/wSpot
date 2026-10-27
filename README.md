# wSpot — Phase 3 (Construction Week 7: Custom Fuzzy Matching)

Builds on Phase 2. Replaces the `LIKE`-based substring search with the
custom fuzzy-matching scorer from the blueprint, and changes how search
is architected: apps are now loaded into memory once instead of being
re-queried from SQLite on every keystroke.

## What's new since Phase 2 (+ the keyboard/click fix)
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

Nothing else changed — `MainWindow.xaml`/`.xaml.cs` here are the same
files from the keyboard/click fix, not modified further in this phase.

## What this does differently from Phase 2
- Typing `vsc` now finds "Visual Studio Code" even though those letters
  aren't consecutive — Phase 2's substring match required typing a
  literal chunk of the real name.
- Results are ranked by match quality, not alphabetically within a
  `LIKE` filter.

## Known simplifications (intentional, flagged for later)
- **Greedy matching, not globally optimal.** The scorer takes the first
  available match for each typed character rather than searching every
  possible alignment. This occasionally picks a technically-valid but
  not-ideal alignment (e.g. matching the "s" inside "Vi**s**ual" instead
  of the "S" starting "Studio" for a query like "vsc"). A fully optimal
  version needs dynamic programming — real added complexity for a gain
  that's rarely noticeable at this project's scale. Worth knowing for
  your report as a deliberate scope decision, not an oversight.
- **No typo tolerance yet** (Levenshtein distance) — the blueprint flags
  this as optional/if-time-allows, not core to this week.
- **`_database` field in MainViewModel is currently unused** after the
  constructor — it's kept deliberately for Week 9, which will need it
  to write usage stats on each launch.

## Next step (Construction Week 8)
Integrate Everything's CLI for file search, merging its results into
the same scored-and-ranked list `FuzzyMatcher` already produces for
apps — tag each result with its type (app vs. file) so the UI can show
different icons once Week 10's polish pass adds them.

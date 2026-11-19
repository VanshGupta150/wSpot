using System;
using System.Collections.Generic;
using System.Linq;
using wSpot.Models;

namespace wSpot.Services
{
    /// <summary>One row the ranker chose: its score, and which letters of the name matched (to draw in bold).</summary>
    public sealed record RankedResult(AppEntry Entry, int Score, IEnumerable<int>? MatchedIndices);

    /// <summary>
    /// Turns "what was typed" plus "everything we know about" into the ordered list
    /// the user sees. It has no UI, database or clock access (the clock arrives as a
    /// parameter), so every rule here can be tested with plain objects.
    ///
    /// Ordering rule: score = fuzzy match quality + a scaled bonus for apps and commands
    /// + a frecency boost for things the user opens a lot - depth penalty for deep paths.
    /// A valid calculation always goes first. The web-search row is always last.
    /// </summary>
    public static class ResultRanker
    {
        public const int MaxResults = 15;      // rows in the list, not counting the web-search row
        public const int AppScoreBonus = 10;   // base bonus for apps before scaling
        public const int MinFuzzyScore = 5;    // matches weaker than this are dropped

        // Far above anything the fuzzy matcher can produce, so a valid calculation is always the first row.
        private const int CalculatorScore = 100_000;

        /// <param name="keepUnmatchedFiles">
        /// True for a fresh result set: Everything already decided those files match
        /// (it may have matched on the folder name), so they stay even if the fuzzy
        /// matcher finds nothing in the file name. False when re-checking files left
        /// over from an earlier query: those must still match, or they drop out.
        /// </param>
        public static List<RankedResult> Rank(
            string query,
            IReadOnlyList<AppEntry> apps,
            IReadOnlyList<AppEntry> commands,
            IReadOnlyList<AppEntry> filesAndFolders,
            bool keepUnmatchedFiles,
            IReadOnlyDictionary<string, UsageRecord> usage,
            DateTime nowUtc)
        {
            var ranked = new List<RankedResult>();

            if (query.Length == 0)
                return ranked;

            // 1. A calculation, if that is what was typed.
            if (ExpressionEvaluator.TryEvaluate(query, out double calculated))
                ranked.Add(new RankedResult(CreateCalculatorEntry(calculated), CalculatorScore, null));

            // Sets for app deduplication and collision avoidance.
            var appTargets = new HashSet<string>(apps.Select(a => a.TargetPath), StringComparer.OrdinalIgnoreCase);
            var appNames = new HashSet<string>(apps.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);

            bool searchingUninstaller = query.Contains("uninst", StringComparison.OrdinalIgnoreCase);

            // 2. Installed apps.
            foreach (AppEntry app in apps)
            {
                // Suppress uninstallers from cluttering normal app searches unless explicitly searching for them.
                bool isUninstaller = app.Name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)
                                  || app.Name.EndsWith("Uninstaller", StringComparison.OrdinalIgnoreCase);
                if (isUninstaller && !searchingUninstaller)
                    continue;

                int? score = FuzzyMatcher.Score(query, app.Name);
                if (!score.HasValue || score.Value < MinFuzzyScore) continue;

                // Scale the bonus: strong matches get the full bonus, weak ones get less,
                // so a scattered match doesn't leapfrog a solid file match.
                int bonus = Math.Min(AppScoreBonus, Math.Max(2, score.Value / 3));
                int total = score.Value + bonus + FrecencyBoost(app, usage, nowUtc);
                ranked.Add(new RankedResult(app, total, FuzzyMatcher.MatchedIndices(query, app.Name)));
            }

            // 3. Quick commands. Matching their keywords too means "reboot" finds Restart.
            //    A command is skipped when an installed app has the same name.
            foreach (AppEntry command in commands)
            {
                if (appNames.Contains(command.Name)) continue;

                int? best = FuzzyMatcher.Score(query, command.Name);
                int[]? indices = best.HasValue ? FuzzyMatcher.MatchedIndices(query, command.Name) : null;

                foreach (string keyword in SplitKeywords(command.Keywords))
                {
                    int? keywordScore = FuzzyMatcher.Score(query, keyword);

                    if (keywordScore.HasValue && (!best.HasValue || keywordScore.Value > best.Value))
                        best = keywordScore;
                }

                if (!best.HasValue || best.Value < MinFuzzyScore) continue;

                int bonus = Math.Min(AppScoreBonus, Math.Max(2, best.Value / 3));
                int total = best.Value + bonus + FrecencyBoost(command, usage, nowUtc);
                ranked.Add(new RankedResult(command, total, indices));
            }

            // 4. Files and folders. File names often use "_" or "-" where a person would type a space.
            string compactQuery = new string(query.Where(c => !char.IsWhiteSpace(c)).ToArray());

            foreach (AppEntry item in filesAndFolders)
            {
                // Deduplicate: if an app with the same target or name is already in the Start Menu index, drop the file copy.
                if (item.Kind == ResultKind.App && (appTargets.Contains(item.TargetPath) || appNames.Contains(item.Name)))
                    continue;

                int boost = FrecencyBoost(item, usage, nowUtc);
                int? score = FuzzyMatcher.Score(compactQuery, item.Name);

                if (score.HasValue && score.Value >= MinFuzzyScore)
                {
                    // Depth penalty: shallow paths outrank deeply nested ones.
                    int depth = item.TargetPath.Count(c => c == '\\' || c == '/');
                    int depthPenalty = Math.Max(0, depth - 3);

                    // Apps found via file search (e.g. Spotify on Desktop) get the scaled app bonus too.
                    int appBonus = item.Kind == ResultKind.App
                        ? Math.Min(AppScoreBonus, Math.Max(2, score.Value / 3))
                        : 0;

                    int total = score.Value + appBonus + boost - depthPenalty;
                    ranked.Add(new RankedResult(item, total, FuzzyMatcher.MatchedIndices(compactQuery, item.Name)));
                }
                else if (score.HasValue)
                {
                    // Below minimum threshold: drop loose/weak matches
                }
                else if (keepUnmatchedFiles)
                {
                    ranked.Add(new RankedResult(item, boost, null));
                }
            }

            // 5. Best first. OrderByDescending is stable, so equal scores keep insertion order.
            List<RankedResult> top = ranked
                .OrderByDescending(r => r.Score)
                .Take(MaxResults)
                .ToList();

            // 6. The web search fallback is always the last row.
            top.Add(new RankedResult(WebSearch.CreateEntry(query), 0, null));

            return top;
        }

        /// <summary>
        /// What to show in the empty search box: the things launched most (and most
        /// recently), best first. Commands that need confirmation are left out so a
        /// quick Enter can never restart the PC; files that have been deleted are left out.
        /// </summary>
        public static List<AppEntry> Suggest(
            IReadOnlyList<AppEntry> apps,
            IReadOnlyList<AppEntry> commands,
            IReadOnlyDictionary<string, UsageRecord> usage,
            DateTime nowUtc,
            Func<string, bool> itemExists,
            int count = 5)
        {
            var known = new Dictionary<string, AppEntry>(StringComparer.Ordinal);

            foreach (AppEntry app in apps) known.TryAdd(app.UsageKey, app);
            foreach (AppEntry command in commands) known.TryAdd(command.UsageKey, command);

            var scored = new List<(UsageRecord Record, double Current)>();

            foreach (UsageRecord record in usage.Values)
            {
                double current = FrecencyScorer.CurrentScore(record, nowUtc);

                if (current >= FrecencyScorer.MinSuggestionScore)
                    scored.Add((record, current));
            }

            var suggestions = new List<AppEntry>();

            foreach (var (record, _) in scored.OrderByDescending(s => s.Current))
            {
                if (suggestions.Count >= count) break;

                AppEntry? entry = null;

                if (known.TryGetValue(record.Key, out AppEntry? match))
                {
                    entry = match;
                }
                else if ((record.Kind == ResultKind.File || record.Kind == ResultKind.Folder) && itemExists(record.TargetPath))
                {
                    entry = new AppEntry { Kind = record.Kind, Name = record.Name, TargetPath = record.TargetPath };
                }

                if (entry is null || entry.RequiresConfirmation) continue;

                suggestions.Add(entry);
            }

            return suggestions;
        }

        private static int FrecencyBoost(AppEntry entry, IReadOnlyDictionary<string, UsageRecord> usage, DateTime nowUtc)
        {
            if (!entry.TrackUsage) return 0;

            if (usage.TryGetValue(entry.UsageKey, out UsageRecord? record))
                return FrecencyScorer.RankingBoost(FrecencyScorer.CurrentScore(record, nowUtc));

            return 0;
        }

        private static IEnumerable<string> SplitKeywords(string keywords) =>
            keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static AppEntry CreateCalculatorEntry(double value)
        {
            string text = ExpressionEvaluator.Format(value);

            return new AppEntry
            {
                Kind = ResultKind.Calculator,
                Name = text,
                TargetPath = text,
                Description = "Press Enter to copy the result",
                TrackUsage = false
            };
        }
    }
}

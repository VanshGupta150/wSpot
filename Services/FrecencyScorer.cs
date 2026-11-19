using System;
using wSpot.Models;

namespace wSpot.Services
{
    /// <summary>
    /// "Frecency" = frequency + recency, in one number.
    ///
    /// Every item has a score. Launching it adds 1. The score then loses half of its
    /// value every <see cref="HalfLifeDays"/> days, so:
    ///   - something opened five times today scores higher than something opened five
    ///     times last month (recency), and
    ///   - something opened every day scores higher than something opened once (frequency).
    ///
    /// Only two numbers are stored per item (the score and the moment it was last
    /// updated). That is enough, because the decay is exponential: decaying "now" is
    /// the same as decaying in steps. No launch history has to be kept.
    ///
    /// Everything here is a pure function with no database or clock access, so it can
    /// be unit-tested directly.
    /// </summary>
    public static class FrecencyScorer
    {
        /// <summary>After this many days without a launch, an item's score halves.</summary>
        public const double HalfLifeDays = 14.0;

        /// <summary>Items scoring below this are not offered as suggestions in the empty box.</summary>
        public const double MinSuggestionScore = 0.5;

        // Ranking boost = BoostScale * ln(1 + score), capped at MaxBoost.
        // The logarithm makes the first few launches count the most: one launch is
        // worth +8, three +17, ten recent launches +29, a month of daily use +34.
        //
        // Usage is meant to win close calls, not clear ones. For one or two letters
        // every match scores about the same, so usage decides. The longer the query,
        // the wider the gap between a clean match and a scattered one: 38 to 95 points
        // in the 6- and 7-letter examples tried. Daily use is worth at most +37 (the
        // cap of +45 needs 42 or more recent launches), so a favourite can close a
        // gap of about 37 points but not a gap of 50 or more.
        private const double BoostScale = 12.0;
        private const int MaxBoost = 45;

        /// <summary>The score as it is at <paramref name="nowUtc"/>, given it was <paramref name="score"/> at <paramref name="updatedUtc"/>.</summary>
        public static double Decay(double score, DateTime updatedUtc, DateTime nowUtc)
        {
            double elapsedDays = (nowUtc - updatedUtc).TotalDays;

            // A clock set backwards must not inflate scores.
            if (elapsedDays <= 0) return score;

            return score * Math.Pow(0.5, elapsedDays / HalfLifeDays);
        }

        public static double CurrentScore(UsageRecord record, DateTime nowUtc) =>
            Decay(record.Score, record.ScoreUpdatedUtc, nowUtc);

        /// <summary>
        /// The record to save after <paramref name="entry"/> was launched at
        /// <paramref name="nowUtc"/>: the old score decayed up to now, plus one.
        /// <paramref name="existing"/> is null the first time an item is launched.
        /// </summary>
        public static UsageRecord RecordLaunch(UsageRecord? existing, AppEntry entry, DateTime nowUtc)
        {
            double previous = existing is null ? 0.0 : CurrentScore(existing, nowUtc);
            int launchCount = (existing?.LaunchCount ?? 0) + 1;

            return new UsageRecord(
                entry.UsageKey,
                entry.Kind,
                entry.Name,
                entry.TargetPath,
                launchCount,
                previous + 1.0,
                nowUtc,
                nowUtc);
        }

        /// <summary>Points added to a search result's score for this much frecency.</summary>
        public static int RankingBoost(double currentScore)
        {
            if (currentScore <= 0) return 0;

            double boost = BoostScale * Math.Log(1.0 + currentScore);
            return (int)Math.Min(MaxBoost, Math.Round(boost));
        }
    }
}

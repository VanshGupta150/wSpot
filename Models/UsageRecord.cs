using System;

namespace wSpot.Models
{
    /// <summary>
    /// One row of the usage_stats table: how often, and how recently, the user has
    /// launched one particular app, file or command.
    ///
    /// Score is a "decayed launch counter" (see FrecencyScorer). It is only correct
    /// as of ScoreUpdatedUtc; to get today's value, decay it by the time since then.
    /// </summary>
    public sealed record UsageRecord(
        string Key,                    // AppEntry.UsageKey: kind + identity, lower-cased
        ResultKind Kind,
        string Name,                   // kept so a suggestion can be rebuilt without searching again
        string TargetPath,
        int LaunchCount,               // lifetime total, for information only (never decays)
        double Score,                  // decayed counter as of ScoreUpdatedUtc
        DateTime ScoreUpdatedUtc,
        DateTime LastLaunchedUtc);
}

using System;
using wSpot.Models;
using wSpot.Services;
using Xunit;

namespace wSpot.Tests
{
    public class FrecencyScorerTests
    {
        [Fact]
        public void Decay_ZeroElapsed_ReturnsSameScore()
        {
            var now = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            double score = FrecencyScorer.Decay(10.0, now, now);
            Assert.Equal(10.0, score);
        }

        [Fact]
        public void Decay_ClockSetBackwards_ReturnsSameScore()
        {
            var past = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
            var updated = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            double score = FrecencyScorer.Decay(10.0, updated, past);
            Assert.Equal(10.0, score);
        }

        [Fact]
        public void Decay_OneHalfLife_HalvesScore()
        {
            var updated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var now = updated.AddDays(FrecencyScorer.HalfLifeDays);
            double score = FrecencyScorer.Decay(10.0, updated, now);
            Assert.Equal(5.0, score, precision: 4);
        }

        [Fact]
        public void Decay_TwoHalfLives_QuarterScore()
        {
            var updated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var now = updated.AddDays(FrecencyScorer.HalfLifeDays * 2);
            double score = FrecencyScorer.Decay(10.0, updated, now);
            Assert.Equal(2.5, score, precision: 4);
        }

        [Fact]
        public void RecordLaunch_FirstTime_CreatesRecordWithScoreOne()
        {
            var now = DateTime.UtcNow;
            var entry = new AppEntry
            {
                Name = "Notepad",
                TargetPath = @"C:\Windows\notepad.exe",
                Kind = ResultKind.App
            };

            var record = FrecencyScorer.RecordLaunch(null, entry, now);

            Assert.Equal(entry.UsageKey, record.Key);
            Assert.Equal(1, record.LaunchCount);
            Assert.Equal(1.0, record.Score);
            Assert.Equal(now, record.LastLaunchedUtc);
        }

        [Fact]
        public void RecordLaunch_SubsequentLaunch_IncrementsCountAndDecaysScore()
        {
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var entry = new AppEntry { Name = "Notepad", TargetPath = @"C:\Windows\notepad.exe" };
            var r1 = FrecencyScorer.RecordLaunch(null, entry, t0);

            // Launched again after 14 days (score 1.0 halves to 0.5, then adds 1.0 = 1.5)
            var t1 = t0.AddDays(14);
            var r2 = FrecencyScorer.RecordLaunch(r1, entry, t1);

            Assert.Equal(2, r2.LaunchCount);
            Assert.Equal(1.5, r2.Score, precision: 4);
        }

        [Theory]
        [InlineData(0.0, 0)]
        [InlineData(-1.0, 0)]
        [InlineData(1.0, 8)]
        [InlineData(3.0, 17)]
        [InlineData(10.0, 29)]
        public void RankingBoost_CalculatesExpectedBoost(double currentScore, int expectedBoost)
        {
            int boost = FrecencyScorer.RankingBoost(currentScore);
            Assert.Equal(expectedBoost, boost);
        }

        [Fact]
        public void RankingBoost_CappedAtMaxBoost()
        {
            int boost = FrecencyScorer.RankingBoost(1000.0);
            Assert.Equal(45, boost);
        }
    }
}

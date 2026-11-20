using System;
using System.Collections.Generic;
using wSpot.Models;
using wSpot.Services;
using Xunit;

namespace wSpot.Tests
{
    public class ResultRankerTests
    {
        [Fact]
        public void Rank_CalculatorExpression_CalculationIsFirstRow()
        {
            var apps = new List<AppEntry>
            {
                new AppEntry { Name = "Calculator", TargetPath = "calc.exe" }
            };
            var emptyCommands = new List<AppEntry>();
            var emptyFiles = new List<AppEntry>();
            var emptyUsage = new Dictionary<string, UsageRecord>();

            var results = ResultRanker.Rank(
                "2+3*4",
                apps,
                emptyCommands,
                emptyFiles,
                keepUnmatchedFiles: false,
                emptyUsage,
                DateTime.UtcNow);

            Assert.NotEmpty(results);
            Assert.Equal(ResultKind.Calculator, results[0].Entry.Kind);
            Assert.Equal("14", results[0].Entry.Name);
        }

        [Fact]
        public void Rank_AlwaysAppendsWebSearchAsLastRow()
        {
            var apps = new List<AppEntry>
            {
                new AppEntry { Name = "Notepad", TargetPath = "notepad.exe" }
            };

            var results = ResultRanker.Rank(
                "note",
                apps,
                new List<AppEntry>(),
                new List<AppEntry>(),
                false,
                new Dictionary<string, UsageRecord>(),
                DateTime.UtcNow);

            Assert.True(results.Count >= 2);
            var last = results[^1];
            Assert.Equal(ResultKind.WebSearch, last.Entry.Kind);
        }

        [Fact]
        public void Rank_FrecencyBoost_PushesFrequentlyUsedAppToTop()
        {
            var now = DateTime.UtcNow;
            var appA = new AppEntry { Name = "NoteTab", TargetPath = "notetab.exe" };
            var appB = new AppEntry { Name = "Notepad", TargetPath = "notepad.exe" };
            var apps = new List<AppEntry> { appA, appB };

            // Give Notepad a strong usage history (10 launches)
            var usage = new Dictionary<string, UsageRecord>
            {
                [appB.UsageKey] = new UsageRecord(appB.UsageKey, ResultKind.App, appB.Name, appB.TargetPath, 10, 10.0, now, now)
            };

            var results = ResultRanker.Rank(
                "note",
                apps,
                new List<AppEntry>(),
                new List<AppEntry>(),
                false,
                usage,
                now);

            // Notepad should rank above NoteTab due to frecency
            Assert.Equal("Notepad", results[0].Entry.Name);
        }

        [Fact]
        public void Suggest_DangerousCommandsWithConfirmation_ExcludedFromSuggestions()
        {
            var now = DateTime.UtcNow;
            var safeApp = new AppEntry { Name = "Notepad", TargetPath = "notepad.exe" };
            var restartCmd = new AppEntry
            {
                Name = "Restart",
                Kind = ResultKind.Command,
                RequiresConfirmation = true
            };

            var usage = new Dictionary<string, UsageRecord>
            {
                [safeApp.UsageKey] = new UsageRecord(safeApp.UsageKey, ResultKind.App, safeApp.Name, safeApp.TargetPath, 5, 5.0, now, now),
                [restartCmd.UsageKey] = new UsageRecord(restartCmd.UsageKey, ResultKind.Command, restartCmd.Name, "", 10, 10.0, now, now)
            };

            var suggestions = ResultRanker.Suggest(
                new List<AppEntry> { safeApp },
                new List<AppEntry> { restartCmd },
                usage,
                now,
                itemExists: _ => true,
                count: 5);

            Assert.Contains(suggestions, s => s.Name == "Notepad");
            Assert.DoesNotContain(suggestions, s => s.Name == "Restart");
        }
    }
}

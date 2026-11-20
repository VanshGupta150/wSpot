using wSpot.Services;
using Xunit;

namespace wSpot.Tests
{
    public class FuzzyMatcherTests
    {
        [Fact]
        public void Score_MatchingQuery_ReturnsPositiveScore()
        {
            int? score = FuzzyMatcher.Score("not", "Notepad");
            Assert.NotNull(score);
            Assert.True(score.Value > 0);
        }

        [Fact]
        public void Score_NonMatchingQuery_ReturnsNull()
        {
            int? score = FuzzyMatcher.Score("xyz", "Notepad");
            Assert.Null(score);
        }

        [Fact]
        public void Score_WordStartMatches_ScoresHigherThanInsideWord()
        {
            int? wordStart = FuzzyMatcher.Score("note", "Notepad");
            int? insideWord = FuzzyMatcher.Score("pad", "Notepad");

            Assert.NotNull(wordStart);
            Assert.NotNull(insideWord);
            Assert.True(wordStart.Value > insideWord.Value);
        }

        [Fact]
        public void MatchedIndices_ValidMatch_ReturnsCorrectIndices()
        {
            // "gc" in "Google Chrome": G is at index 0, C is at index 7
            int[]? indices = FuzzyMatcher.MatchedIndices("gc", "Google Chrome");

            Assert.NotNull(indices);
            Assert.Equal(2, indices.Length);
            Assert.Equal(0, indices[0]);
            Assert.Equal(7, indices[1]);
        }

        [Fact]
        public void MatchedIndices_NoMatch_ReturnsNull()
        {
            int[]? indices = FuzzyMatcher.MatchedIndices("abc", "Notepad");
            Assert.Null(indices);
        }

        [Fact]
        public void MatchedIndices_EmptyQuery_ReturnsEmptyArray()
        {
            int[]? indices = FuzzyMatcher.MatchedIndices("", "Notepad");
            Assert.NotNull(indices);
            Assert.Empty(indices);
        }
    }
}

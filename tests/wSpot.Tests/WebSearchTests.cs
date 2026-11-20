using wSpot.Models;
using wSpot.Services;
using Xunit;

namespace wSpot.Tests
{
    public class WebSearchTests
    {
        [Theory]
        [InlineData("hello world", "https://www.google.com/search?q=hello%20world")]
        [InlineData("c# programming", "https://www.google.com/search?q=c%23%20programming")]
        [InlineData("  trim test  ", "https://www.google.com/search?q=trim%20test")]
        public void BuildUrl_EscapesAndFormatsCorrectly(string query, string expectedUrl)
        {
            string url = WebSearch.BuildUrl(query);
            Assert.Equal(expectedUrl, url);
        }

        [Fact]
        public void CreateEntry_SetsCorrectProperties()
        {
            var entry = WebSearch.CreateEntry("sample query");

            Assert.Equal(ResultKind.WebSearch, entry.Kind);
            Assert.Equal("Search the web for \"sample query\"", entry.Name);
            Assert.Equal(WebSearch.BuildUrl("sample query"), entry.TargetPath);
            Assert.False(entry.TrackUsage);
        }
    }
}

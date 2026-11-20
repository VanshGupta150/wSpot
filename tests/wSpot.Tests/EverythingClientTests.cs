using System.Collections.Generic;
using wSpot.Services;
using Xunit;

namespace wSpot.Tests
{
    public class EverythingClientTests
    {
        [Fact]
        public void BuildTerms_SplitsWordsAndStripsQuotesAndLeadingDashes()
        {
            var terms = EverythingClient.BuildTerms("  \"project report\"  -draft /final  ");

            Assert.Equal(4, terms.Count);
            Assert.Equal("project", terms[0]);
            Assert.Equal("report", terms[1]);
            Assert.Equal("draft", terms[2]);
            Assert.Equal("final", terms[3]);
        }

        [Fact]
        public void BuildTerms_EmptyString_ReturnsEmptyList()
        {
            var terms = EverythingClient.BuildTerms("   ");
            Assert.Empty(terms);
        }

        [Fact]
        public void ParsePaths_FiltersInvalidAndDeduplicates()
        {
            var input = new List<string>
            {
                @"C:\Projects\wSpot\file1.txt",
                "Not a valid path",
                @"\relative\path\file.txt",
                "",
                @"c:\projects\wspot\file1.txt", // duplicate (case-insensitive)
                @"\\Server\Share\doc.pdf",     // network UNC path
                @"D:\Photos\vacation.jpg"
            };

            var parsed = EverythingClient.ParsePaths(input);

            Assert.Equal(3, parsed.Count);
            Assert.Equal(@"C:\Projects\wSpot\file1.txt", parsed[0]);
            Assert.Equal(@"\\Server\Share\doc.pdf", parsed[1]);
            Assert.Equal(@"D:\Photos\vacation.jpg", parsed[2]);
        }
    }
}

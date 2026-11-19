using System;
using wSpot.Models;

namespace wSpot.Services
{
    /// <summary>
    /// The last row of every search: "Search the web for ...". When nothing on the PC
    /// matches, it is the only row, so Enter still does something useful.
    /// To use another search engine, change the address in <see cref="BuildUrl"/>.
    /// </summary>
    public static class WebSearch
    {
        public static string BuildUrl(string query) =>
            "https://www.google.com/search?q=" + Uri.EscapeDataString(query.Trim());

        public static AppEntry CreateEntry(string query)
        {
            string trimmed = query.Trim();

            return new AppEntry
            {
                Kind = ResultKind.WebSearch,
                Name = $"Search the web for \"{trimmed}\"",
                Description = "Opens your default browser",
                TargetPath = BuildUrl(trimmed),
                TrackUsage = false // searching for different things isn't a "favourite"
            };
        }
    }
}

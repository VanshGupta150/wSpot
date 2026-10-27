namespace wSpot.Services
{
    /// <summary>
    /// Scores how well a typed query matches a target string, using
    /// subsequence matching (query characters must appear in the target,
    /// in order, but not necessarily consecutively) plus bonuses for:
    ///   - matching at the start of a word (e.g. the "V" in "Visual")
    ///   - runs of consecutive matched characters
    ///   - matching the exact letter case typed
    /// and a small penalty for longer targets, so a tighter, shorter match
    /// outranks a looser one buried in a longer name.
    ///
    /// This greedily takes the FIRST available match for each query
    /// character rather than searching for the best possible alignment —
    /// a known, intentional simplification. A fully optimal matcher would
    /// need dynamic programming to compare every possible alignment, which
    /// is real added complexity for a gain that's rarely noticeable in
    /// practice at this project's scale (dozens to a few hundred apps).
    /// </summary>
    public static class FuzzyMatcher
    {
        /// <summary>
        /// Returns a score if every character in <paramref name="query"/>
        /// appears in <paramref name="target"/> in order, or null if it
        /// doesn't match at all. Higher scores are better matches.
        /// </summary>
        public static int? Score(string query, string target)
        {
            if (string.IsNullOrEmpty(query)) return 0;
            if (string.IsNullOrEmpty(target)) return null;

            int score = 0;
            int targetIndex = 0;
            int consecutiveBonus = 0;
            bool previousWasMatch = false;
            bool previousWasSeparator = true; // the very start of a word counts as a word start

            foreach (char queryChar in query)
            {
                bool found = false;

                while (targetIndex < target.Length)
                {
                    char targetChar = target[targetIndex];
                    bool isMatch = char.ToLowerInvariant(targetChar) == char.ToLowerInvariant(queryChar);

                    if (isMatch)
                    {
                        found = true;
                        score += 1;

                        if (targetChar == queryChar)
                            score += 1; // typed the exact case

                        if (previousWasSeparator)
                            score += 8; // matched right at a word boundary

                        if (previousWasMatch)
                        {
                            consecutiveBonus += 4;
                            score += consecutiveBonus; // reward runs, growing with their length
                        }
                        else
                        {
                            consecutiveBonus = 0;
                        }

                        previousWasMatch = true;
                        previousWasSeparator = !char.IsLetterOrDigit(targetChar);
                        targetIndex++;
                        break;
                    }

                    previousWasMatch = false;
                    previousWasSeparator = !char.IsLetterOrDigit(targetChar);
                    targetIndex++;
                }

                if (!found) return null; // this query character never appears in what's left of target
            }

            score -= target.Length / 4; // shorter targets carry less "noise" around the match

            return score;
        }
    }
}

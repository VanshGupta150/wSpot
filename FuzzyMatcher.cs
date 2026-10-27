namespace wSpot.Services
{
    public static class FuzzyMatcher
    {
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

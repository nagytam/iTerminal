namespace iConsole.Helper;

/// <summary>The result of <see cref="FuzzyMatcher.Match"/>: how well the candidate fits, and which of its characters matched.</summary>
/// <param name="Score">Higher is better. Zero is never returned for a match.</param>
/// <param name="Positions">Indexes into the candidate, ascending, one per pattern character.</param>
public readonly record struct FuzzyMatch(int Score, IReadOnlyList<int> Positions);

/// <summary>
/// Subsequence matching with a score, for command palettes, completion lists and type-to-search: the pattern's
/// characters must appear in the candidate in order, not next to each other. Case is ignored. Consecutive
/// characters, a match at the start of a word (after a space, separator or a lower-to-upper change) and a match
/// at the start of the candidate score higher; gaps cost a little.
/// </summary>
public static class FuzzyMatcher
{
    private const int ConsecutiveBonus = 8;
    private const int WordStartBonus = 10;
    private const int FirstCharacterBonus = 6;
    private const int GapPenalty = 1;
    private const int BaseScore = 1;
    private const int None = int.MinValue;

    /// <summary>Matches <paramref name="pattern"/> against <paramref name="candidate"/>; null when it is not a subsequence. An empty pattern matches everything with score 1.</summary>
    public static FuzzyMatch? Match(string pattern, string candidate)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return new FuzzyMatch(1, []);
        }

        if (string.IsNullOrEmpty(candidate) || pattern.Length > candidate.Length)
        {
            return null;
        }

        // best[i, j]: the top score with pattern[i] matched at candidate[j]; from[i, j] is where pattern[i - 1] sat.
        // The table picks the highest-scoring alignment, not just the leftmost one.
        int n = candidate.Length;
        int m = pattern.Length;
        int[,] best = new int[m, n];
        int[,] from = new int[m, n];
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < n; j++)
            {
                best[i, j] = None;
                from[i, j] = -1;
            }
        }

        for (int i = 0; i < m; i++)
        {
            for (int j = i; j < n; j++)
            {
                if (char.ToLowerInvariant(pattern[i]) != char.ToLowerInvariant(candidate[j]))
                {
                    continue;
                }

                int own = CharacterScore(candidate, j);
                if (i == 0)
                {
                    best[i, j] = own;
                    continue;
                }

                for (int k = i - 1; k < j; k++)
                {
                    if (best[i - 1, k] == None)
                    {
                        continue;
                    }

                    int gap = j - k - 1;
                    int score = best[i - 1, k] + own + (gap == 0 ? ConsecutiveBonus : -gap * GapPenalty);
                    if (score > best[i, j])
                    {
                        best[i, j] = score;
                        from[i, j] = k;
                    }
                }
            }
        }

        int bestEnd = -1;
        for (int j = m - 1; j < n; j++)
        {
            if (best[m - 1, j] != None && (bestEnd < 0 || best[m - 1, j] > best[m - 1, bestEnd]))
            {
                bestEnd = j;
            }
        }

        if (bestEnd < 0)
        {
            return null;
        }

        int[] positions = new int[m];
        int at = bestEnd;
        for (int i = m - 1; i >= 0; i--)
        {
            positions[i] = at;
            at = from[i, at];
        }

        return new FuzzyMatch(Math.Max(1, best[m - 1, bestEnd]), positions);
    }

    /// <summary>The items that match, best first; equal scores keep their original order.</summary>
    public static IReadOnlyList<(T Item, FuzzyMatch Match)> Rank<T>(string pattern, IEnumerable<T> items, Func<T, string> text)
    {
        return items
            .Select((item, index) => (item, index, match: Match(pattern, text(item))))
            .Where(entry => entry.match is not null)
            .OrderByDescending(entry => entry.match!.Value.Score)
            .ThenBy(entry => entry.index)
            .Select(entry => (entry.item, entry.match!.Value))
            .ToList();
    }

    private static int CharacterScore(string candidate, int index)
    {
        int score = BaseScore;
        if (index == 0)
        {
            return score + FirstCharacterBonus + WordStartBonus;
        }

        char previous = candidate[index - 1];
        char current = candidate[index];
        if (!char.IsLetterOrDigit(previous) || (char.IsLower(previous) && char.IsUpper(current)))
        {
            score += WordStartBonus;
        }

        return score;
    }
}

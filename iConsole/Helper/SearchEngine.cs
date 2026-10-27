using System.Text.RegularExpressions;
using iConsole.Data;

namespace iConsole.Helper;

/// <summary>One place a search found something: where it starts and how long it is.</summary>
public readonly record struct SearchHit(int Line, int Column, int Length);

/// <summary>
/// Finds text in lines, as plain text or as a regular expression. A regular expression is matched line by line, so a pattern
/// cannot span lines, and an empty match (<c>a*</c> where there is no <c>a</c>) is not a hit. A pattern that does not compile
/// gives no hits and an error message instead of throwing, and a pattern that takes too long on a line is given up on.
/// </summary>
public static class SearchEngine
{
    /// <summary>The most hits one search returns, so a pattern that matches everything cannot fill the memory.</summary>
    public const int MaxHits = 100_000;

    /// <summary>How long one line may take to match before the search stops with an error.</summary>
    public static readonly TimeSpan LineTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>All hits of <paramref name="pattern"/> in <paramref name="lines"/>, in order. <paramref name="error"/> says why there are none for a bad pattern.</summary>
    public static IReadOnlyList<SearchHit> FindAll(IEnumerable<string> lines, string pattern, bool useRegex, bool matchCase, out string? error)
    {
        error = null;
        var hits = new List<SearchHit>();
        if (string.IsNullOrEmpty(pattern))
        {
            return hits;
        }

        Regex? regex = null;
        if (useRegex)
        {
            try
            {
                RegexOptions options = RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
                regex = new Regex(pattern, options, LineTimeout);
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
                return hits;
            }
        }

        StringComparison comparison = matchCase ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase;
        int row = 0;
        foreach (string line in lines)
        {
            try
            {
                if (regex is not null)
                {
                    foreach (Match match in regex.Matches(line))
                    {
                        if (match.Length > 0 && !Add(hits, row, match.Index, match.Length))
                        {
                            return hits;
                        }
                    }
                }
                else
                {
                    int at = 0;
                    while ((at = line.IndexOf(pattern, at, comparison)) >= 0)
                    {
                        if (!Add(hits, row, at, pattern.Length))
                        {
                            return hits;
                        }

                        at++;
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                error = "The pattern took too long on line " + (row + 1);
                return hits;
            }

            row++;
        }

        return hits;
    }

    private static bool Add(List<SearchHit> hits, int row, int column, int length)
    {
        hits.Add(new SearchHit(row, column, length));
        return hits.Count < MaxHits;
    }

    /// <summary>Replaces every match in <paramref name="line"/> and says how many there were. <paramref name="replacement"/> may use <c>$1</c> and <c>${name}</c> for a regular expression.</summary>
    public static string Replace(string line, string pattern, string replacement, bool useRegex, bool matchCase, out int count)
    {
        count = 0;
        if (string.IsNullOrEmpty(pattern))
        {
            return line;
        }

        if (!useRegex)
        {
            StringComparison comparison = matchCase ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase;
            int at = 0;
            while ((at = line.IndexOf(pattern, at, comparison)) >= 0)
            {
                count++;
                at += pattern.Length;
            }

            return count == 0 ? line : line.Replace(pattern, replacement, comparison);
        }

        RegexOptions options = RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        var regex = new Regex(pattern, options, LineTimeout);
        int replaced = 0;
        string result = regex.Replace(line, match =>
        {
            if (match.Length == 0)
            {
                return match.Value;
            }

            replaced++;
            return match.Result(replacement);
        });
        count = replaced;
        return result;
    }
}

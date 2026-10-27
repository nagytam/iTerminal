using System.Globalization;

namespace iConsole.Helper;

/// <summary>Checks spelling and offers corrections. Words are the letters (and apostrophes inside a word) that <see cref="SpellChecker.Words"/> finds.</summary>
public interface ISpellChecker
{
    /// <summary>True when <paramref name="word"/> is spelled right, or is something the checker does not judge (a number, an identifier).</summary>
    bool IsCorrect(string word);

    /// <summary>Up to <paramref name="max"/> words that <paramref name="word"/> might have been meant to be, best first; none for a correct word.</summary>
    IReadOnlyList<string> Suggest(string word, int max = 5);
}

/// <summary>
/// A spell checker over a list of words. It knows exactly the words it is given: there is no affix expansion, so the list should
/// hold every form (a "full form" word list such as SCOWL or <c>/usr/share/dict/words</c>), or <c>running</c> will be flagged
/// when only <c>run</c> is listed. Case is ignored, a possessive <c>'s</c> is allowed, a hyphenated word is right when its parts
/// are, and words with digits, underscores or in capitals (acronyms) are not judged. The user's own words
/// (<see cref="Add"/>) and the ones skipped for this session (<see cref="Ignore"/>) count as right.
/// </summary>
public sealed class SpellChecker : ISpellChecker
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz";

    /// <summary>Where a word list is looked for on a system that has one.</summary>
    public static readonly IReadOnlyList<string> SystemDictionaryPaths =
    [
        "/usr/share/dict/words",
        "/usr/share/dict/american-english",
        "/usr/share/dict/british-english",
        "/usr/share/dict/cracklib-small",
    ];

    private readonly Lock _gate = new();
    private readonly HashSet<string> _words;
    private readonly HashSet<string> _user = [];
    private readonly HashSet<string> _ignored = [];

    public SpellChecker(IEnumerable<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        _words = new HashSet<string>(words.Select(Normalize).Where(w => w.Length > 0), StringComparer.Ordinal);
    }

    /// <summary>The number of words in the list, without the user's.</summary>
    public int Count => _words.Count;

    /// <summary>
    /// Reads a word list: one word per line, or a Hunspell <c>.dic</c> file (a count on the first line and <c>word/FLAGS</c> after
    /// it; the flags are dropped). Blank lines and lines starting with <c>#</c> are skipped.
    /// </summary>
    public static SpellChecker FromFile(string path)
    {
        var words = new List<string>();
        bool first = true;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (first)
            {
                first = false;
                if (int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    continue;
                }
            }

            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            int flags = line.IndexOf('/');
            words.Add(flags >= 0 ? line[..flags] : line);
        }

        return new SpellChecker(words);
    }

    /// <summary>The first word list of <see cref="SystemDictionaryPaths"/> that exists, or null.</summary>
    public static SpellChecker? TryLoadSystemDictionary()
    {
        foreach (string path in SystemDictionaryPaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    return FromFile(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static string Normalize(string word) => word.Trim().ToLowerInvariant();

    /// <summary>Adds a word to the user's dictionary; returns whether it was new.</summary>
    public bool Add(string word)
    {
        string normal = Normalize(word);
        lock (_gate)
        {
            return normal.Length > 0 && _user.Add(normal);
        }
    }

    /// <summary>Skips a word for as long as this checker lives.</summary>
    public bool Ignore(string word)
    {
        string normal = Normalize(word);
        lock (_gate)
        {
            return normal.Length > 0 && _ignored.Add(normal);
        }
    }

    /// <summary>The user's own words, in order.</summary>
    public IReadOnlyList<string> UserWords
    {
        get { lock (_gate) { return [.. _user.Order()]; } }
    }

    private bool Known(string normal)
    {
        lock (_gate)
        {
            return _words.Contains(normal) || _user.Contains(normal) || _ignored.Contains(normal);
        }
    }

    public bool IsCorrect(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return true;
        }

        string trimmed = word.Trim('\'', '’');
        if (trimmed.Length < 2 || trimmed.Any(c => char.IsDigit(c) || c == '_') || IsAcronym(trimmed) || IsMixedCase(trimmed))
        {
            return true;
        }

        string normal = Normalize(trimmed.Replace('’', '\''));
        if (Known(normal))
        {
            return true;
        }

        if (normal.EndsWith("'s", StringComparison.Ordinal) && Known(normal[..^2]))
        {
            return true;
        }

        if (normal.EndsWith("s'", StringComparison.Ordinal) && Known(normal[..^1]))
        {
            return true;
        }

        return normal.Contains('-') && normal.Split('-', StringSplitOptions.RemoveEmptyEntries).All(part => part.Length < 2 || Known(part));
    }

    private static bool IsAcronym(string word) => word.Length > 1 && word.All(char.IsUpper);

    /// <summary>camelCase, PascalCase and iPhone: a capital after the first letter marks a name, not a word.</summary>
    private static bool IsMixedCase(string word) => word.Skip(1).Any(char.IsUpper);

    public IReadOnlyList<string> Suggest(string word, int max = 5)
    {
        if (IsCorrect(word) || max <= 0)
        {
            return [];
        }

        string normal = Normalize(word.Replace('’', '\''));
        var found = new List<string>();
        foreach (string candidate in Edits(normal))
        {
            if (Known(candidate) && !found.Contains(candidate))
            {
                found.Add(candidate);
            }
        }

        if (found.Count < max && normal.Length <= 12)
        {
            // Two edits away, only from the first-edit strings, and only as many as are still needed.
            foreach (string once in Edits(normal).Take(3000))
            {
                foreach (string candidate in Edits(once))
                {
                    if (Known(candidate) && !found.Contains(candidate))
                    {
                        found.Add(candidate);
                    }
                }

                if (found.Count >= max * 4)
                {
                    break;
                }
            }
        }

        // Closer words first, then the ones that keep the first letter, then alphabetical so the order is the same every time.
        return [.. found
            .OrderBy(candidate => Distance(normal, candidate))
            .ThenBy(candidate => candidate.Length > 0 && normal.Length > 0 && candidate[0] == normal[0] ? 0 : 1)
            .ThenBy(candidate => candidate, StringComparer.Ordinal)
            .Take(max)
            .Select(candidate => MatchCase(word, candidate))];
    }

    private static string MatchCase(string original, string suggestion)
    {
        if (original.Length > 0 && char.IsUpper(original[0]) && suggestion.Length > 0)
        {
            return char.ToUpperInvariant(suggestion[0]) + suggestion[1..];
        }

        return suggestion;
    }

    /// <summary>Every string one edit (a deletion, a swap of neighbours, a replacement or an insertion) away from <paramref name="word"/>.</summary>
    private static IEnumerable<string> Edits(string word)
    {
        for (int i = 0; i < word.Length; i++)
        {
            yield return word.Remove(i, 1);
        }

        for (int i = 0; i < word.Length - 1; i++)
        {
            yield return word[..i] + word[i + 1] + word[i] + word[(i + 2)..];
        }

        for (int i = 0; i < word.Length; i++)
        {
            foreach (char c in Alphabet)
            {
                if (c != word[i])
                {
                    yield return word[..i] + c + word[(i + 1)..];
                }
            }
        }

        for (int i = 0; i <= word.Length; i++)
        {
            foreach (char c in Alphabet)
            {
                yield return word[..i] + c + word[i..];
            }
        }
    }

    /// <summary>Edit distance between two short words (Damerau–Levenshtein, optimal string alignment).</summary>
    public static int Distance(string a, string b)
    {
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
        }

        return d[a.Length, b.Length];
    }

    /// <summary>The words in <paramref name="text"/>: runs of letters, with an apostrophe or hyphen allowed between letters.</summary>
    public static IEnumerable<(int Start, int Length)> Words(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (!char.IsLetter(text[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < text.Length
                && (char.IsLetterOrDigit(text[i]) || text[i] == '_'
                    || ((text[i] is '\'' or '’' or '-') && i + 1 < text.Length && char.IsLetter(text[i + 1]))))
            {
                i++;
            }

            yield return (start, i - start);
        }
    }
}

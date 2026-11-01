using System.Globalization;
using iConsole.Localization;

namespace iConsole.Csv;

/// <summary>A query the parser rejected. The message says what is wrong, in words a person can act on.</summary>
public sealed class CsvQueryException(string message) : Exception(message);

/// <summary>What a query returned: the columns it selected, the rows after its filter, order and limit, and how many rows matched before the limit.</summary>
public sealed record CsvQueryResult(IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows, int Matched);

/// <summary>
/// A small SQL-like query over a <see cref="CsvDocument"/>:
/// <c>SELECT name, amount WHERE amount &gt; 10 AND city = 'Pécs' ORDER BY amount DESC LIMIT 20</c>.
/// <list type="bullet">
/// <item><c>SELECT *</c> or a list of column names. The <c>SELECT</c> may be left out: a query that starts with a condition filters the rows.</item>
/// <item>Conditions: <c>=</c> <c>==</c> <c>!=</c> <c>&lt;&gt;</c> <c>&lt;</c> <c>&lt;=</c> <c>&gt;</c> <c>&gt;=</c>, <c>LIKE</c> (with <c>%</c> and <c>_</c>),
/// <c>CONTAINS</c> (case-insensitive text), and <c>IS [NOT] EMPTY</c>. Joined by <c>AND</c> and <c>OR</c>, where AND binds
/// tighter (there are no parentheses).</item>
/// <item>Two values compare as numbers when both are numbers, otherwise as text ignoring case.</item>
/// <item>Column names are matched ignoring case. A name with spaces goes in double quotes or square brackets; a string value
/// goes in single quotes.</item>
/// <item><c>ORDER BY column [ASC|DESC]</c> and <c>LIMIT n</c>.</item>
/// </list>
/// </summary>
public static class CsvQuery
{
    /// <summary>Runs <paramref name="text"/> over the document. An empty query selects every column and row.</summary>
    /// <exception cref="CsvQueryException">The query is not valid, or names a column the file does not have.</exception>
    public static CsvQueryResult Run(CsvDocument document, string text)
    {
        var parser = new Parser(Tokenize(text), document.Headers);
        Plan plan = parser.ParsePlan();

        List<string[]> matched = [.. document.Rows.Where(row => plan.Filter(row))];
        int total = matched.Count;
        if (plan.OrderBy is { } order)
        {
            // A row with nothing in the column goes last whichever way the rest is sorted: descending, the empty ones would
            // otherwise head a column of amounts.
            List<string[]> filled = [.. matched.Where(row => Field(row, order).Length > 0)];
            List<string[]> blank = [.. matched.Where(row => Field(row, order).Length == 0)];
            filled = plan.Descending
                ? [.. filled.OrderByDescending(row => Field(row, order), Comparer)]
                : [.. filled.OrderBy(row => Field(row, order), Comparer)];
            matched = [.. filled, .. blank];
        }

        if (plan.Limit is { } limit)
        {
            matched = matched.Take(limit).ToList();
        }

        IReadOnlyList<string> headers = plan.Columns is null
            ? document.Headers
            : [.. plan.Columns.Select(index => document.Headers[index])];
        List<string[]> rows = plan.Columns is null
            ? matched
            : [.. matched.Select(row => plan.Columns.Select(index => Field(row, index)).ToArray())];
        return new CsvQueryResult(headers, rows, total);
    }

    /// <summary>The field at <paramref name="index"/>, or empty when the row is shorter than the header.</summary>
    internal static string Field(string[] row, int index) => index < row.Length ? row[index] : string.Empty;

    /// <summary>
    /// Whether the text matches a LIKE pattern: <c>%</c> is any run of characters (also none), <c>_</c> is any one, anything
    /// else matches itself ignoring case. Done by walking both strings, which takes time in proportion to their lengths
    /// however many wildcards the pattern holds; a regular expression built from the pattern could take for ever on one.
    /// </summary>
    public static bool GlobMatch(string text, string pattern)
    {
        int t = 0;
        int p = 0;
        int star = -1;
        int resume = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] == '%')
            {
                star = p++;
                resume = t;
            }
            else if (p < pattern.Length && (pattern[p] == '_' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t])))
            {
                p++;
                t++;
            }
            else if (star >= 0)
            {
                // Let the last % take one more character and try the rest of the pattern again from there.
                p = star + 1;
                t = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '%')
        {
            p++;
        }

        return p == pattern.Length;
    }

    /// <summary>Compares two field values: as numbers when both are numbers, otherwise as text ignoring case.</summary>
    internal static int Compare(string left, string right)
    {
        if (TryNumber(left, out double a) && TryNumber(right, out double b))
        {
            return a.CompareTo(b);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The order of a sorted column: numbers first, by value, then everything else as text ignoring case. A column that mixes
    /// numbers and words has to be sorted by one rule that is the same for every pair; comparing two numbers as numbers and
    /// a number with a word as text made "10", "9" and "1a" each smaller than the next, which sorts to no particular order.
    /// </summary>
    public static int CompareForOrder(string left, string right)
    {
        bool leftIsNumber = TryNumber(left, out double a);
        bool rightIsNumber = TryNumber(right, out double b);
        if (leftIsNumber && rightIsNumber)
        {
            return a.CompareTo(b);
        }

        if (leftIsNumber != rightIsNumber)
        {
            return leftIsNumber ? -1 : 1;
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly IComparer<string> Comparer = Comparer<string>.Create(CompareForOrder);

    // "NaN" and "Infinity" parse as doubles, but a name or a word in a column is not a number.
    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private enum Kind
    {
        Word,
        Text,
        Identifier,
        Symbol,
    }

    private sealed record Token(Kind Kind, string Value, int Position);

    private static List<Token> Tokenize(string text)
    {
        List<Token> tokens = [];
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            int start = i;
            if (c is '\'' or '"' or '[')
            {
                char close = c == '[' ? ']' : c;
                System.Text.StringBuilder content = new();
                int end = i + 1;
                while (end < text.Length)
                {
                    if (text[end] == close)
                    {
                        // A doubled quote inside quotes is the quote itself, as in SQL: 'O''Brien'.
                        if (c != '[' && end + 1 < text.Length && text[end + 1] == close)
                        {
                            content.Append(close);
                            end += 2;
                            continue;
                        }

                        break;
                    }

                    content.Append(text[end]);
                    end++;
                }

                if (end >= text.Length)
                {
                    throw new CsvQueryException(string.Format(UiText.Get("Csv.Error.Unterminated", "A quote is not closed: missing {0}"), close));
                }

                // Single quotes are a value; double quotes and brackets name a column.
                tokens.Add(new Token(c == '\'' ? Kind.Text : Kind.Identifier, content.ToString(), start));
                i = end + 1;
                continue;
            }

            if (c is '=' or '!' or '<' or '>' or ',' or '*')
            {
                char next = i + 1 < text.Length ? text[i + 1] : '\0';
                int length = (c, next) switch
                {
                    ('<', '=' or '>') or ('>', '=') or ('!', '=') or ('=', '=') => 2,
                    _ => 1,
                };
                if (c == '!' && length == 1)
                {
                    throw new CsvQueryException(string.Format(UiText.Get("Csv.Error.Expected", "Expected {0}, found {1}"), "!=", "!"));
                }

                tokens.Add(new Token(Kind.Symbol, text.Substring(i, length), start));
                i += length;
                continue;
            }

            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('=' or '!' or '<' or '>' or ',' or '*' or '\'' or '"' or '['))
            {
                i++;
            }

            tokens.Add(new Token(Kind.Word, text[start..i], start));
        }

        return tokens;
    }

    /// <summary>What a query does, once parsed: the filter, the columns to show (null for all), the order and the limit.</summary>
    private sealed record Plan(Func<string[], bool> Filter, int[]? Columns, int? OrderBy, bool Descending, int? Limit);

    private sealed class Parser(List<Token> tokens, IReadOnlyList<string> headers)
    {
        private int _position;

        public Plan ParsePlan()
        {
            int[]? columns = null;
            if (IsKeyword("SELECT"))
            {
                _position++;
                columns = ParseSelectList();
            }

            if (IsKeyword("FROM"))
            {
                // The file is the only table: FROM and the name after it are accepted so a familiar query runs, and ignored.
                _position++;
                if (Peek() is { Kind: not Kind.Symbol } && !IsKeyword("WHERE") && !IsKeyword("ORDER") && !IsKeyword("LIMIT"))
                {
                    _position++;
                }
            }

            Func<string[], bool> filter = _ => true;
            if (IsKeyword("WHERE"))
            {
                _position++;
                filter = ParseCondition();
            }
            else if (Peek() is not null && !IsKeyword("ORDER") && !IsKeyword("LIMIT"))
            {
                // A query that starts with a condition is a filter: WHERE is only a word that says so.
                filter = ParseCondition();
            }

            int? orderBy = null;
            bool descending = false;
            if (IsKeyword("ORDER"))
            {
                _position++;
                ExpectKeyword("BY");
                orderBy = ResolveColumn(ReadIdentifier());
                if (IsKeyword("DESC"))
                {
                    descending = true;
                    _position++;
                }
                else if (IsKeyword("ASC"))
                {
                    _position++;
                }
            }

            int? limit = null;
            if (IsKeyword("LIMIT"))
            {
                _position++;
                Token number = Next("a number after LIMIT");
                if (!int.TryParse(number.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 0)
                {
                    throw new CsvQueryException(UiText.Get("Csv.Error.Limit", "LIMIT needs a whole number"));
                }

                limit = count;
            }

            if (Peek() is { } rest)
            {
                throw Unexpected("the end of the query", rest);
            }

            return new Plan(filter, columns, orderBy, descending, limit);
        }

        private int[]? ParseSelectList()
        {
            if (Peek() is { Kind: Kind.Symbol, Value: "*" })
            {
                _position++;
                return null;
            }

            List<int> columns = [];
            do
            {
                if (columns.Count > 0)
                {
                    _position++; // the comma
                }

                columns.Add(ResolveColumn(ReadIdentifier()));
            }
            while (Peek() is { Kind: Kind.Symbol, Value: "," });

            return [.. columns];
        }

        private Func<string[], bool> ParseCondition()
        {
            List<List<Func<string[], bool>>> groups = [[ParseTerm()]];
            while (IsKeyword("AND") || IsKeyword("OR"))
            {
                bool and = IsKeyword("AND");
                _position++;
                Func<string[], bool> term = ParseTerm();
                if (and)
                {
                    groups[^1].Add(term);
                }
                else
                {
                    groups.Add([term]);
                }
            }

            // AND binds tighter than OR: the query is true when any group of ANDed terms is true.
            return row => groups.Any(group => group.All(term => term(row)));
        }

        private Func<string[], bool> ParseTerm()
        {
            int column = ResolveColumn(ReadIdentifier());

            if (IsKeyword("IS"))
            {
                _position++;
                bool negate = IsKeyword("NOT");
                if (negate)
                {
                    _position++;
                }

                ExpectKeyword("EMPTY");
                return row => (CsvQuery.Field(row, column).Length == 0) != negate;
            }

            if (IsKeyword("NOT") && PeekAt(1) is { } after && IsWord(after, "LIKE"))
            {
                _position += 2;
                return Like(column, ReadValue(), negate: true);
            }

            if (IsKeyword("LIKE"))
            {
                _position++;
                return Like(column, ReadValue(), negate: false);
            }

            if (IsKeyword("CONTAINS"))
            {
                _position++;
                string part = ReadValue();
                return row => CsvQuery.Field(row, column).Contains(part, StringComparison.OrdinalIgnoreCase);
            }

            Token op = Next("an operator such as = or >");
            if (op.Kind != Kind.Symbol || op.Value is "," or "*")
            {
                throw Unexpected("an operator such as = or >", op);
            }

            string value = ReadValue();
            return op.Value switch
            {
                "=" or "==" => row => CsvQuery.Compare(CsvQuery.Field(row, column), value) == 0,
                "!=" or "<>" => row => CsvQuery.Compare(CsvQuery.Field(row, column), value) != 0,
                "<" => row => CsvQuery.Compare(CsvQuery.Field(row, column), value) < 0,
                "<=" => row => CsvQuery.Compare(CsvQuery.Field(row, column), value) <= 0,
                ">" => row => CsvQuery.Compare(CsvQuery.Field(row, column), value) > 0,
                ">=" => row => CsvQuery.Compare(CsvQuery.Field(row, column), value) >= 0,
                _ => throw Unexpected("an operator such as = or >", op),
            };
        }

        private static Func<string[], bool> Like(int column, string pattern, bool negate) =>
            row => GlobMatch(CsvQuery.Field(row, column), pattern) != negate;

        /// <summary>A value: a quoted string, or a number or word taken as text.</summary>
        private string ReadValue()
        {
            Token token = Next("a value");
            if (token.Kind == Kind.Symbol)
            {
                throw Unexpected("a value", token);
            }

            return token.Value;
        }

        private string ReadIdentifier()
        {
            Token token = Next("a column name");
            if (token.Kind == Kind.Text || token.Kind == Kind.Symbol)
            {
                throw Unexpected("a column name", token);
            }

            return token.Value;
        }

        private int ResolveColumn(string name)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                if (string.Equals(headers[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            string available = string.Join(", ", headers.Take(12)) + (headers.Count > 12 ? ", …" : string.Empty);
            throw new CsvQueryException(string.Format(UiText.Get("Csv.Error.UnknownColumn", "No column called '{0}'. Columns: {1}"), name, available));
        }

        private Token Next(string expected)
        {
            if (_position >= tokens.Count)
            {
                throw new CsvQueryException(string.Format(UiText.Get("Csv.Error.Expected", "Expected {0}, found {1}"), expected, UiText.Get("Csv.Error.EndOfQuery", "the end of the query")));
            }

            return tokens[_position++];
        }

        private Token? Peek() => _position < tokens.Count ? tokens[_position] : null;

        private Token? PeekAt(int offset) => _position + offset < tokens.Count ? tokens[_position + offset] : null;

        private bool IsKeyword(string keyword) => Peek() is { } token && IsWord(token, keyword);

        private static bool IsWord(Token token, string keyword) =>
            token.Kind == Kind.Word && string.Equals(token.Value, keyword, StringComparison.OrdinalIgnoreCase);

        private void ExpectKeyword(string keyword)
        {
            if (!IsKeyword(keyword))
            {
                throw Unexpected(keyword, Peek());
            }

            _position++;
        }

        private static CsvQueryException Unexpected(string expected, Token? found) =>
            Unexpected(expected, found is null ? UiText.Get("Csv.Error.EndOfQuery", "the end of the query") : $"'{found.Value}'");

        private static CsvQueryException Unexpected(string expected, string found) =>
            new(string.Format(UiText.Get("Csv.Error.Expected", "Expected {0}, found {1}"), expected, found));
    }
}

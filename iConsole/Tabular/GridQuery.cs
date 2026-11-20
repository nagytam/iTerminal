using System.Globalization;
using System.Text;
using iConsole.Localization;

namespace iConsole.Tabular;

/// <summary>A query the parser rejected. The message says what is wrong, in words a person can act on.</summary>
public sealed class GridQueryException(string message) : Exception(message);

/// <summary>What a query returned: the columns it selected, the rows after its filter, order and limit, and how many rows matched before the limit.</summary>
/// <param name="Truncated">True when the rows were cut before the query saw them all (a long form of an array read to its cell cap).</param>
public sealed record GridQueryResult(IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows, int Matched, bool Truncated = false);

/// <summary>
/// The one query language every grid viewer takes (CSV, Parquet, HDF5 slices and SQLite): a small SQL-like text such as
/// <c>SELECT name, amount WHERE amount &gt; 10 AND city = 'Pécs' ORDER BY amount DESC LIMIT 20</c>.
/// <list type="bullet">
/// <item><c>SELECT *</c> or a list of column names. The <c>SELECT</c> may be left out: a query that starts with a condition filters the rows.</item>
/// <item><c>FROM</c> names the table. A file has one table, so its name is accepted and ignored; SQLite uses it.</item>
/// <item>Conditions: <c>=</c> <c>==</c> <c>!=</c> <c>&lt;&gt;</c> <c>&lt;</c> <c>&lt;=</c> <c>&gt;</c> <c>&gt;=</c>, <c>LIKE</c> (with <c>%</c> and <c>_</c>),
/// <c>CONTAINS</c> (case-insensitive text), and <c>IS [NOT] EMPTY</c>. Joined by <c>AND</c> and <c>OR</c>, where AND binds
/// tighter (there are no parentheses).</item>
/// <item>Two values compare as numbers when both are numbers, otherwise as text ignoring case.</item>
/// <item>Column names are matched ignoring case. A name with spaces goes in double quotes or square brackets; a string value
/// goes in single quotes.</item>
/// <item><c>ORDER BY column [ASC|DESC]</c> and <c>LIMIT n</c>.</item>
/// </list>
/// <para>
/// The same plan runs two ways: <see cref="Execute"/> over rows already in memory, and <see cref="ToSql"/>, which renders it as
/// one SQLite statement for a table. The SQL keeps the same meaning for text columns; for numbers stored in a text column SQLite
/// compares by its own affinity rules, which is the one place the two differ.
/// </para>
/// </summary>
public static class GridQuery
{
    /// <summary>Runs <paramref name="text"/> over rows in memory. An empty query selects every column and row.</summary>
    /// <exception cref="GridQueryException">The query is not valid, or names a column the table does not have.</exception>
    public static GridQueryResult Run(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, string text) =>
        Execute(headers, rows, Parse(text, headers));

    /// <summary>Parses <paramref name="text"/> against the column names of the table.</summary>
    /// <exception cref="GridQueryException">The query is not valid, or names a column the table does not have.</exception>
    public static GridQueryPlan Parse(string text, IReadOnlyList<string> headers) => new Parser(Tokenize(text), headers).ParsePlan();

    /// <summary>Runs a parsed plan over rows in memory.</summary>
    public static GridQueryResult Execute(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, GridQueryPlan plan)
    {
        Func<string[], bool> filter = plan.Where is { } where ? Compile(where, headers) : _ => true;
        List<string[]> matched = [.. rows.Where(filter)];
        int total = matched.Count;
        if (plan.OrderBy is { } orderName)
        {
            int order = IndexOf(headers, orderName);
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

        if (plan.Columns is null)
        {
            return new GridQueryResult(headers, matched, total);
        }

        int[] columns = [.. plan.Columns.Select(name => IndexOf(headers, name))];
        IReadOnlyList<string> selected = [.. plan.Columns];
        List<string[]> shaped = [.. matched.Select(row => columns.Select(index => Field(row, index)).ToArray())];
        return new GridQueryResult(selected, shaped, total);
    }

    /// <summary>
    /// The plan as one SQLite statement over <paramref name="table"/>, with every name quoted and every value written as a quoted text
    /// literal. A value is never written bare: a bare number would compare as a number, and a text column holding <c>0123</c> would then
    /// stop matching <c>'0123'</c>. SQLite applies the column's own type to a quoted value, so <c>amount &gt; '10'</c> compares as a
    /// number on a numeric column. Blank values sort last, as they do in memory.
    /// </summary>
    public static string ToSql(GridQueryPlan plan, string table)
    {
        StringBuilder sql = new("SELECT ");
        sql.Append(plan.Columns is null ? "*" : string.Join(", ", plan.Columns.Select(QuoteIdentifier)));
        sql.Append(" FROM ").Append(QuoteIdentifier(table));
        if (plan.Where is { Groups.Count: > 0 } where)
        {
            sql.Append(" WHERE ").Append(string.Join(" OR ", where.Groups.Select(group => "(" + string.Join(" AND ", group.Select(TermToSql)) + ")")));
        }

        if (plan.OrderBy is { } order)
        {
            string column = QuoteIdentifier(order);
            sql.Append($" ORDER BY ({column} IS NULL OR {column} = '') ASC, {column} {(plan.Descending ? "DESC" : "ASC")}");
        }

        if (plan.Limit is { } limit)
        {
            sql.Append(" LIMIT ").Append(limit.ToString(CultureInfo.InvariantCulture));
        }

        return sql.ToString();
    }

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

    /// <summary>The field at <paramref name="index"/>, or empty when the row is shorter than the header.</summary>
    internal static string Field(string[] row, int index) => index < row.Length ? row[index] : string.Empty;

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

    private static int IndexOf(IReadOnlyList<string> headers, string name)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new GridQueryException(string.Format(UiText.Get("Csv.Error.UnknownColumn", "No column called '{0}'. Columns: {1}"), name, Available(headers)));
    }

    private static string Available(IReadOnlyList<string> headers) =>
        string.Join(", ", headers.Take(12)) + (headers.Count > 12 ? ", …" : string.Empty);

    private static Func<string[], bool> Compile(GridCondition condition, IReadOnlyList<string> headers)
    {
        List<List<Func<string[], bool>>> groups = [.. condition.Groups.Select(group => group.Select(term => CompileTerm(term, headers)).ToList())];
        // AND binds tighter than OR: the query is true when any group of ANDed terms is true.
        return row => groups.Any(group => group.All(term => term(row)));
    }

    private static Func<string[], bool> CompileTerm(GridTerm term, IReadOnlyList<string> headers)
    {
        int column = IndexOf(headers, term.Column);
        switch (term)
        {
            case GridIsEmpty empty:
                return row => (Field(row, column).Length == 0) != empty.Negate;
            case GridLike like:
                return row => GlobMatch(Field(row, column), like.Pattern) != like.Negate;
            case GridContains contains:
                return row => Field(row, column).Contains(contains.Text, StringComparison.OrdinalIgnoreCase);
            case GridComparison comparison:
                {
                    string value = comparison.Value;
                    return comparison.Operator switch
                    {
                        "=" => row => Compare(Field(row, column), value) == 0,
                        "!=" => row => Compare(Field(row, column), value) != 0,
                        // A blank field is no value, so it is neither smaller nor larger than one (as ORDER BY, it goes last).
                        "<" => row => Ordered(row, column, value, c => c < 0),
                        "<=" => row => Ordered(row, column, value, c => c <= 0),
                        ">" => row => Ordered(row, column, value, c => c > 0),
                        _ => row => Ordered(row, column, value, c => c >= 0),
                    };
                }
            default:
                throw new InvalidOperationException($"Unknown grid term {term.GetType().Name}.");
        }
    }

    private static bool Ordered(string[] row, int column, string value, Func<int, bool> test)
    {
        string field = Field(row, column);
        return (field.Length > 0 || value.Length == 0) && test(Compare(field, value));
    }

    private static string TermToSql(GridTerm term)
    {
        string column = QuoteIdentifier(term.Column);
        return term switch
        {
            GridIsEmpty empty => empty.Negate
                ? $"({column} IS NOT NULL AND {column} <> '')"
                : $"({column} IS NULL OR {column} = '')",
            GridLike like => $"{column} {(like.Negate ? "NOT LIKE" : "LIKE")} {QuoteText(like.Pattern)}",
            GridContains contains => $"instr(lower(CAST({column} AS TEXT)), lower({QuoteText(contains.Text)})) > 0",
            GridComparison comparison => $"{column} {SqlOperator(comparison.Operator)} {QuoteText(comparison.Value)}",
            _ => throw new InvalidOperationException($"Unknown grid term {term.GetType().Name}."),
        };
    }

    private static string SqlOperator(string op) => op == "!=" ? "<>" : op;

    private static string QuoteText(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string QuoteIdentifier(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

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
                StringBuilder content = new();
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
                    throw new GridQueryException(string.Format(UiText.Get("Csv.Error.Unterminated", "A quote is not closed: missing {0}"), close));
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
                    throw new GridQueryException(string.Format(UiText.Get("Csv.Error.Expected", "Expected {0}, found {1}"), "!=", "!"));
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

    private sealed class Parser(List<Token> tokens, IReadOnlyList<string> headers)
    {
        private int _position;

        public GridQueryPlan ParsePlan()
        {
            IReadOnlyList<string>? columns = null;
            if (IsKeyword("SELECT"))
            {
                _position++;
                columns = ParseSelectList();
            }

            string? from = null;
            if (IsKeyword("FROM"))
            {
                // The table name is taken as written; a file has one table, so for a file it is accepted and ignored.
                _position++;
                if (Peek() is { Kind: not Kind.Symbol } name && !IsKeyword("WHERE") && !IsKeyword("ORDER") && !IsKeyword("LIMIT"))
                {
                    from = name.Value;
                    _position++;
                }
            }

            GridCondition? where = null;
            if (IsKeyword("WHERE"))
            {
                _position++;
                where = ParseCondition();
            }
            else if (Peek() is not null && !IsKeyword("ORDER") && !IsKeyword("LIMIT"))
            {
                // A query that starts with a condition is a filter: WHERE is only a word that says so.
                where = ParseCondition();
            }

            string? orderBy = null;
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
                    throw new GridQueryException(UiText.Get("Csv.Error.Limit", "LIMIT needs a whole number"));
                }

                limit = count;
            }

            if (Peek() is { } rest)
            {
                throw Unexpected("the end of the query", rest);
            }

            return new GridQueryPlan(from, columns, where, orderBy, descending, limit);
        }

        private IReadOnlyList<string>? ParseSelectList()
        {
            if (Peek() is { Kind: Kind.Symbol, Value: "*" })
            {
                _position++;
                return null;
            }

            List<string> columns = [];
            do
            {
                if (columns.Count > 0)
                {
                    _position++; // the comma
                }

                columns.Add(ResolveColumn(ReadIdentifier()));
            }
            while (Peek() is { Kind: Kind.Symbol, Value: "," });

            return columns;
        }

        private GridCondition ParseCondition()
        {
            List<List<GridTerm>> groups = [[ParseTerm()]];
            while (IsKeyword("AND") || IsKeyword("OR"))
            {
                bool and = IsKeyword("AND");
                _position++;
                GridTerm term = ParseTerm();
                if (and)
                {
                    groups[^1].Add(term);
                }
                else
                {
                    groups.Add([term]);
                }
            }

            return new GridCondition(groups);
        }

        private GridTerm ParseTerm()
        {
            string column = ResolveColumn(ReadIdentifier());

            if (IsKeyword("IS"))
            {
                _position++;
                bool negate = IsKeyword("NOT");
                if (negate)
                {
                    _position++;
                }

                ExpectKeyword("EMPTY");
                return new GridIsEmpty(column, negate);
            }

            if (IsKeyword("NOT") && PeekAt(1) is { } after && IsWord(after, "LIKE"))
            {
                _position += 2;
                return new GridLike(column, ReadValue(), Negate: true);
            }

            if (IsKeyword("LIKE"))
            {
                _position++;
                return new GridLike(column, ReadValue(), Negate: false);
            }

            if (IsKeyword("CONTAINS"))
            {
                _position++;
                return new GridContains(column, ReadValue());
            }

            Token op = Next("an operator such as = or >");
            if (op.Kind != Kind.Symbol || op.Value is "," or "*")
            {
                throw Unexpected("an operator such as = or >", op);
            }

            string canonical = op.Value switch
            {
                "=" or "==" => "=",
                "!=" or "<>" => "!=",
                "<" or "<=" or ">" or ">=" => op.Value,
                _ => throw Unexpected("an operator such as = or >", op),
            };
            return new GridComparison(column, canonical, ReadValue());
        }

        /// <summary>A value: a quoted string, or a number or word taken as text.</summary>
        private string ReadValue()
        {
            Token token = Next("a value");
            if (token.Kind == Kind.Symbol)
            {
                throw Unexpected("a value", token);
            }

            if (token.Value.Contains('\0'))
            {
                throw new GridQueryException(UiText.Get("Csv.Error.Expected", "Expected {0}, found {1}"));
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

        private string ResolveColumn(string name)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                if (string.Equals(headers[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return headers[i];
                }
            }

            throw new GridQueryException(string.Format(UiText.Get("Csv.Error.UnknownColumn", "No column called '{0}'. Columns: {1}"), name, Available(headers)));
        }

        private Token Next(string expected)
        {
            if (_position >= tokens.Count)
            {
                throw new GridQueryException(string.Format(UiText.Get("Csv.Error.Expected", "Expected {0}, found {1}"), expected, UiText.Get("Csv.Error.EndOfQuery", "the end of the query")));
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

        private static GridQueryException Unexpected(string expected, Token? found) =>
            Unexpected(expected, found is null ? UiText.Get("Csv.Error.EndOfQuery", "the end of the query") : $"'{found.Value}'");

        private static GridQueryException Unexpected(string expected, string found) =>
            new(string.Format(UiText.Get("Csv.Error.Expected", "Expected {0}, found {1}"), expected, found));
    }
}

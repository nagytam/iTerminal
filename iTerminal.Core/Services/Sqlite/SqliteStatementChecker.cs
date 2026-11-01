using iConsole.Sqlite;

namespace iTerminal.Core.Services.Sqlite;

/// <summary>
/// Refuses anything that is not one read-only statement, before SQLite sees it. The text is masked first: comments and the
/// contents of quotes and brackets are blanked, so a semicolon or a comment marker inside a string is never read as code.
/// This is the first of three write guards; the read-only connection and <c>query_only</c> are the other two.
/// </summary>
public static class SqliteStatementChecker
{
    private static readonly HashSet<string> AllowedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "WITH", "EXPLAIN", "PRAGMA",
    };

    /// <summary>The pragmas that only read. A pragma that takes an assignment is refused by the '=' rule as well.</summary>
    private static readonly HashSet<string> ReadOnlyPragmas = new(StringComparer.OrdinalIgnoreCase)
    {
        "table_info", "table_list", "table_xinfo", "index_list", "index_info", "foreign_key_list",
        "schema_version", "page_count", "page_size", "integrity_check", "quick_check",
    };

    /// <summary>Throws <see cref="SqlQueryException"/> when <paramref name="sql"/> is not one allowed read-only statement.</summary>
    public static void Validate(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        string masked = Mask(sql);
        int semicolon = masked.IndexOf(';');
        if (semicolon >= 0)
        {
            // A trailing semicolon is fine; anything after it, code or not, is a second statement.
            if (masked[(semicolon + 1)..].Trim().Length > 0)
            {
                throw Refuse("Only one statement can be run at a time");
            }

            masked = masked[..semicolon];
        }

        ValidateStatement(masked);
    }

    private static void ValidateStatement(string statement)
    {
        string text = statement.TrimStart();
        string keyword = Word(text, 0, out int afterKeyword);
        if (!AllowedKeywords.Contains(keyword))
        {
            throw Refuse("Only SELECT, WITH, EXPLAIN and PRAGMA can be run here");
        }

        string rest = text[afterKeyword..];
        if (keyword.Equals("EXPLAIN", StringComparison.OrdinalIgnoreCase))
        {
            string inner = rest.TrimStart();
            if (Word(inner, 0, out int afterQuery).Equals("QUERY", StringComparison.OrdinalIgnoreCase))
            {
                string afterQueryWord = inner[afterQuery..].TrimStart();
                if (!Word(afterQueryWord, 0, out int afterPlan).Equals("PLAN", StringComparison.OrdinalIgnoreCase))
                {
                    throw Refuse("EXPLAIN QUERY must be followed by PLAN");
                }

                inner = afterQueryWord[afterPlan..];
            }

            // EXPLAIN does not run its statement, but the rules still hold for it, recursively.
            ValidateStatement(inner);
            return;
        }

        if (keyword.Equals("PRAGMA", StringComparison.OrdinalIgnoreCase))
        {
            string name = Word(rest.TrimStart(), 0, out _);
            if (!ReadOnlyPragmas.Contains(name))
            {
                throw Refuse($"PRAGMA {name} is not allowed here");
            }

            if (statement.Contains('='))
            {
                throw Refuse("A PRAGMA that sets a value is not allowed here");
            }
        }
    }

    /// <summary>
    /// Returns <paramref name="sql"/> with every comment, and the inside of every quote and bracket, replaced by spaces.
    /// The length is unchanged. An unterminated quote, bracket or block comment is refused.
    /// </summary>
    private static string Mask(string sql)
    {
        char[] chars = sql.ToCharArray();
        int i = 0;
        while (i < sql.Length)
        {
            char c = sql[i];
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                int end = sql.IndexOf('\n', i);
                end = end < 0 ? sql.Length : end;
                Blank(chars, i, end);
                i = end;
            }
            else if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                int close = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    throw Refuse("A comment is not closed");
                }

                Blank(chars, i, close + 2);
                i = close + 2;
            }
            else if (c is '\'' or '"' or '`')
            {
                int end = ClosingQuote(sql, i, c);
                Blank(chars, i, end);
                i = end;
            }
            else if (c == '[')
            {
                int close = sql.IndexOf(']', i + 1);
                if (close < 0)
                {
                    throw Refuse("A [ ] name is not closed");
                }

                Blank(chars, i, close + 1);
                i = close + 1;
            }
            else
            {
                i++;
            }
        }

        return new string(chars);
    }

    /// <summary>The index just past the quote that closes <paramref name="start"/>. A doubled quote inside is an escaped quote.</summary>
    private static int ClosingQuote(string sql, int start, char quote)
    {
        int i = start + 1;
        while (true)
        {
            if (i >= sql.Length)
            {
                throw Refuse("A quoted text is not closed");
            }

            if (sql[i] == quote)
            {
                if (i + 1 < sql.Length && sql[i + 1] == quote)
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }
    }

    private static void Blank(char[] chars, int from, int to)
    {
        for (int k = from; k < to; k++)
        {
            chars[k] = ' ';
        }
    }

    private static string Word(string text, int start, out int end)
    {
        int i = start;
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
        {
            i++;
        }

        end = i;
        return text[start..i];
    }

    private static SqlQueryException Refuse(string message) => new(message);
}

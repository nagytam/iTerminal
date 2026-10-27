using System.Text;
using iTerminal.Core.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace iTerminal.Core.Services.Documents;

/// <summary>
/// The full-text index over extracted document text: a SQLite FTS5 virtual table, driven with raw
/// SQL because a virtual table is not something EF Core can model.
///
/// <para>That it lives outside the EF model is the reason this class exists rather than being three
/// methods on the service. The application has no migrations — a model change drops and recreates
/// the whole database (see <c>DatabaseInitializationHostedService</c>) — and <c>EnsureCreated</c>
/// knows nothing about this table, so it has to be created on demand and, when the metadata rows
/// survived something the index did not, rebuilt from <c>DocumentContents</c>. Every entry point
/// therefore goes through <see cref="EnsureCreated"/> first.</para>
///
/// <para>Why FTS5 and not a scan: the whole point of storing extracted text is that searching it
/// must not mean reading it. A LIKE over a few hundred megabytes of text is seconds of disk per
/// keystroke; an FTS5 <c>MATCH</c> over the same corpus is an index lookup. The LIKE path below is
/// only a fallback for a SQLite build compiled without FTS5, which should not happen with the
/// bundled provider but is cheap to survive.</para>
/// </summary>
internal sealed class DocumentSearchIndex
{
    /// <summary>
    /// Characters of context either side of a match in a snippet, in FTS5's own unit: tokens.
    /// </summary>
    private const int SnippetTokens = 12;

    private const string TableName = "DocumentSearch";

    private readonly Lock _lock = new();
    private bool _checked;
    private bool _available;

    /// <summary>
    /// Whether full-text search is usable. Only meaningful after <see cref="EnsureCreated"/> has
    /// run at least once; the service surfaces it so the dialog can say which mode it is in.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            lock (_lock)
            {
                return _available;
            }
        }
    }

    /// <summary>
    /// Creates the virtual table if it is not there, and repopulates it from the content table when
    /// the two have got out of step. Cheap enough to call on every write path: after the first call
    /// it is a flag check plus, at most, one <c>CREATE TABLE IF NOT EXISTS</c>.
    /// </summary>
    public bool EnsureCreated(iTerminalDbContext context)
    {
        lock (_lock)
        {
            if (_checked)
            {
                return _available;
            }

            _checked = true;

            try
            {
                // 'unicode61' folds case and, with remove_diacritics, matches "resume" against
                // "résumé" — which is what someone searching their own documents expects.
                context.Database.ExecuteSqlRaw(
                    $"CREATE VIRTUAL TABLE IF NOT EXISTS {TableName} USING fts5("
                    + "Path UNINDEXED, Text, tokenize='unicode61 remove_diacritics 2');");

                _available = true;
                RebuildIfEmpty(context);
            }
            catch (SqliteException)
            {
                // A SQLite built without FTS5. Search still works, just by scanning.
                _available = false;
            }
            catch (Exception)
            {
                _available = false;
            }

            return _available;
        }
    }

    /// <summary>
    /// Refills the index from <c>DocumentContents</c> when it is empty but there is text to index.
    ///
    /// <para>This is not a hypothetical. The index is invisible to EF, so anything that recreates
    /// the schema — a model change, a repaired database — can leave a perfectly good content table
    /// behind an index that no longer exists. Without this, search would come back empty until the
    /// next full rescan re-extracted every PDF from scratch, which is minutes of work to rebuild
    /// something that takes seconds.</para>
    /// </summary>
    private void RebuildIfEmpty(iTerminalDbContext context)
    {
        try
        {
            if (CountRows(context, TableName) > 0 || CountRows(context, "DocumentContents") == 0)
            {
                return;
            }

            context.Database.ExecuteSqlRaw(
                $"INSERT INTO {TableName}(Path, Text) SELECT Path, Text FROM DocumentContents;");
        }
        catch (Exception)
        {
            // A rebuild that fails leaves an empty index, which the next scan fills anyway.
        }
    }

    private static long CountRows(iTerminalDbContext context, string table)
    {
        using SqliteCommand command = CreateCommand(context, $"SELECT COUNT(*) FROM {table};");
        object? result = command.ExecuteScalar();
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }

    /// <summary>Replaces the indexed text for one document. Removal first: FTS5 has no upsert.</summary>
    public void Upsert(iTerminalDbContext context, string path, string text)
    {
        if (!EnsureCreated(context))
        {
            return;
        }

        try
        {
            Remove(context, [path]);

            if (text.Length == 0)
            {
                return;
            }

            using SqliteCommand command = CreateCommand(
                context,
                $"INSERT INTO {TableName}(Path, Text) VALUES ($path, $text);");
            command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$text", text);
            command.ExecuteNonQuery();
        }
        catch (Exception)
        {
            // An un-indexed document is still listed and still previewable; only its body is
            // unsearchable until the next scan.
        }
    }

    public void Remove(iTerminalDbContext context, IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0 || !EnsureCreated(context))
        {
            return;
        }

        try
        {
            using SqliteCommand command = CreateCommand(context, $"DELETE FROM {TableName} WHERE Path = $path;");
            SqliteParameter parameter = command.Parameters.Add("$path", SqliteType.Text);

            foreach (string path in paths)
            {
                parameter.Value = path;
                command.ExecuteNonQuery();
            }
        }
        catch (Exception)
        {
            // Leaves a stale row that will be replaced the next time that path is indexed.
        }
    }

    /// <summary>One content hit: which document, a snippet around the match, and its relevance.</summary>
    public readonly record struct ContentHit(string Path, string Snippet, double Rank);

    /// <summary>
    /// Runs <paramref name="query"/> against the indexed text. Returns an empty list rather than
    /// throwing for a query FTS5 cannot parse — the caller is a search box being typed into, and
    /// every prefix of a real query passes through it.
    /// </summary>
    public IReadOnlyList<ContentHit> Search(iTerminalDbContext context, string query, int limit)
    {
        string[] tokens = Tokenize(query);
        if (tokens.Length == 0 || limit <= 0)
        {
            return [];
        }

        if (EnsureCreated(context))
        {
            try
            {
                return SearchFullText(context, tokens, limit);
            }
            catch (SqliteException)
            {
                // A malformed MATCH expression. Falling through to the scan means the user gets
                // results rather than an error they cannot act on.
            }
        }

        return SearchByScan(context, tokens, limit);
    }

    private static IReadOnlyList<ContentHit> SearchFullText(iTerminalDbContext context, string[] tokens, int limit)
    {
        using SqliteCommand command = CreateCommand(
            context,
            $"SELECT Path, snippet({TableName}, 1, '', '', '…', {SnippetTokens}), rank "
            + $"FROM {TableName} WHERE {TableName} MATCH $query ORDER BY rank LIMIT $limit;");

        command.Parameters.AddWithValue("$query", BuildMatchExpression(tokens));
        command.Parameters.AddWithValue("$limit", limit);

        List<ContentHit> hits = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            hits.Add(new ContentHit(
                reader.GetString(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? 0 : reader.GetDouble(2)));
        }

        return hits;
    }

    /// <summary>
    /// The no-FTS5 fallback: a LIKE per token, and a snippet cut out in code. Slow by design —
    /// it exists so that search degrades rather than disappears.
    /// </summary>
    private static IReadOnlyList<ContentHit> SearchByScan(iTerminalDbContext context, string[] tokens, int limit)
    {
        StringBuilder sql = new("SELECT Path, Text FROM DocumentContents WHERE ");
        using SqliteCommand command = CreateCommand(context, string.Empty);

        for (int index = 0; index < tokens.Length; index++)
        {
            if (index > 0)
            {
                sql.Append(" AND ");
            }

            sql.Append($"Text LIKE $token{index}");
            command.Parameters.AddWithValue($"$token{index}", $"%{EscapeLike(tokens[index])}%");
        }

        sql.Append(" LIMIT $limit;");
        command.Parameters.AddWithValue("$limit", limit);
        command.CommandText = sql.ToString();

        List<ContentHit> hits = [];

        try
        {
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string path = reader.GetString(0);
                string text = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                hits.Add(new ContentHit(path, BuildSnippet(text, tokens[0]), 0));
            }
        }
        catch (Exception)
        {
            return [];
        }

        return hits;
    }

    /// <summary>
    /// Turns the user's words into an FTS5 <c>MATCH</c> expression: every word required, each one a
    /// prefix match.
    ///
    /// <para>Each token is quoted, which is what stops FTS5's own operators — <c>AND</c>, <c>NOT</c>,
    /// <c>*</c>, <c>:</c>, a stray quote — from being read as syntax when the user meant them as
    /// text. Prefix matching is what makes the search box usable while it is still being typed:
    /// "invo" finds the invoices.</para>
    /// </summary>
    internal static string BuildMatchExpression(string[] tokens) =>
        string.Join(" AND ", tokens.Select(token => $"\"{token.Replace("\"", "\"\"")}\"*"));

    /// <summary>
    /// Splits a query into search words, dropping the punctuation FTS5 would not have indexed
    /// anyway. Done here rather than left to SQLite so both search paths see the same words.
    /// </summary>
    internal static string[] Tokenize(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        List<string> tokens = [];
        StringBuilder builder = new();

        foreach (char character in query)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                builder.Append(character);
                continue;
            }

            if (builder.Length > 0)
            {
                tokens.Add(builder.ToString());
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            tokens.Add(builder.ToString());
        }

        return [.. tokens];
    }

    /// <summary>A one-line excerpt around the first occurrence of <paramref name="term"/>.</summary>
    internal static string BuildSnippet(string? text, string? term, int radius = 60)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        term ??= string.Empty;
        radius = Math.Max(0, radius);

        int match = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        int start = match < 0 ? 0 : Math.Max(0, match - radius);
        // Long arithmetic: a radius near int.MaxValue wrapped the end below the start.
        int end = (int)Math.Min(text.Length, (long)(match < 0 ? 0 : match) + term.Length + radius);

        string excerpt = text[start..end].Replace('\n', ' ').Replace('\r', ' ').Trim();

        return (start > 0 ? "…" : string.Empty) + excerpt + (end < text.Length ? "…" : string.Empty);
    }

    private static string EscapeLike(string value) =>
        value.Replace("%", string.Empty).Replace("_", string.Empty);

    /// <summary>
    /// A command on the context's own connection, opening it if EF has not already. Sharing the
    /// connection is what keeps the FTS writes in the same transaction as the EF ones.
    /// </summary>
    private static SqliteCommand CreateCommand(iTerminalDbContext context, string sql)
    {
        System.Data.Common.DbConnection connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        SqliteCommand command = (SqliteCommand)connection.CreateCommand();
        command.CommandText = sql;

        if (context.Database.CurrentTransaction?.GetDbTransaction() is SqliteTransaction transaction)
        {
            command.Transaction = transaction;
        }

        return command;
    }
}

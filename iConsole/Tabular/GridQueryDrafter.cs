using iConsole.Localization;

namespace iConsole.Tabular;

/// <summary>
/// The answer to a question: the query, checked against the schema, with the table it reads; or the reason it was not usable,
/// which the viewer puts on its status line.
/// </summary>
public sealed record GridDraft(string? Query, GridQueryPlan? Plan, GridTableSchema? Table, string? Error);

/// <summary>
/// Runs the AI draft behind a query line that starts with <c>?</c>. A question is asked off the UI thread; the viewer polls
/// <see cref="TakeDraft"/> from its idle pass and writes the draft into its query line. The draft is checked with the same parser
/// the query line uses, so a draft that would not run is reported instead of shown. Nothing here runs a query: the user presses
/// Enter, as with any query they type.
/// </summary>
public sealed class GridQueryDrafter(IGridQueryAssistant assistant)
{
    private int _generation;
    private int _pendingGeneration;
    private Task<string>? _request;
    private CancellationTokenSource? _cancellation;
    private Task<GridSchema>? _schemaTask;
    private GridDraft? _ready;

    /// <summary>Whether a question is being answered.</summary>
    public bool IsBusy => _request is not null;

    /// <summary>The answer being waited for, or null. A viewer can await it; a test awaits it instead of polling.</summary>
    public Task<string>? Request => _request;

    /// <summary>Whether the query line holds a question for the assistant rather than a query: it starts with <c>?</c>.</summary>
    public static bool IsQuestion(string text) => text.TrimStart().StartsWith('?');

    /// <summary>The question in a query line: the text after the <c>?</c>, trimmed.</summary>
    public static string QuestionOf(string text) => text.TrimStart()[1..].Trim();

    /// <summary>Why the assistant cannot answer, or null when it can.</summary>
    public string? UnavailableReason => assistant.UnavailableReason;

    /// <summary>Starts answering the question in <paramref name="queryLine"/> against <paramref name="schema"/>. Returns at once.</summary>
    public void Start(string queryLine, GridSchema schema) => Start(queryLine, Task.FromResult(schema));

    /// <summary>
    /// Starts answering against a schema that may still be loading (a SQLite file reads its tables first). The question waits for
    /// the schema on the background task, so this returns at once.
    /// </summary>
    public void Start(string queryLine, Task<GridSchema> schema)
    {
        Cancel();
        int generation = ++_generation;
        _pendingGeneration = generation;
        _schemaTask = schema;
        string question = QuestionOf(queryLine);
        if (question.Length == 0)
        {
            _ready = Failure(UiText.Get("Grid.Ai.NoQuestion", "Type a question after the ?, for example ? rows where amount is over 10"));
            return;
        }

        if (assistant.UnavailableReason is { } reason)
        {
            _ready = Failure(reason);
            return;
        }

        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;
        // The model call is network or disk work: it never runs on the thread that is drawing or handling keys.
        _request = Task.Run(async () => await assistant.DraftAsync(question, await schema.ConfigureAwait(false), token), token);
    }

    /// <summary>
    /// The finished draft, once, from the idle pass. Null while the question is still being answered, and for an answer that was
    /// overtaken by a newer question or a <see cref="Cancel"/>.
    /// </summary>
    public GridDraft? TakeDraft()
    {
        if (_ready is { } ready)
        {
            _ready = null;
            return ready;
        }

        if (_request is not { IsCompleted: true } request)
        {
            return null;
        }

        _request = null;
        _cancellation?.Dispose();
        _cancellation = null;
        if (_pendingGeneration != _generation || request.IsCanceled)
        {
            return null;
        }

        if (request.Exception is { } failure)
        {
            return Failure(failure.InnerException?.Message ?? failure.Message);
        }

        return Check(request.Result, _schemaTask!.Result);
    }

    /// <summary>Drops the question in progress; its answer, when it comes, is never taken.</summary>
    public void Cancel()
    {
        _generation++;
        _ready = null;
        _request = null;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    /// <summary>
    /// Checks a model's answer against the schema. The table is the one a FROM names, or the only table. The FROM is read first,
    /// against every table's columns together, so a column error is reported against the table the answer names, not another one.
    /// </summary>
    internal static GridDraft Check(string answer, GridSchema schema)
    {
        string query = Clean(answer);
        if (query.Length == 0)
        {
            return Failure(UiText.Get("Grid.Ai.NoQuery", "The AI model sent no query"));
        }

        if (schema.Tables.Count == 0)
        {
            return Failure(UiText.Get("Grid.Ai.NoTables", "There are no tables to query"));
        }

        string[] everyColumn = [.. schema.Tables.SelectMany(table => table.Columns.Select(column => column.Name)).Distinct(StringComparer.OrdinalIgnoreCase)];
        GridQueryPlan loose;
        try
        {
            loose = GridQuery.Parse(query, everyColumn);
        }
        catch (GridQueryException exception)
        {
            return Failure(exception.Message);
        }

        GridTableSchema? table = schema.Tables.Count == 1
            ? schema.Tables[0]
            : schema.Tables.FirstOrDefault(candidate => loose.From is not null && string.Equals(loose.From, candidate.Name, StringComparison.OrdinalIgnoreCase));
        if (table is null)
        {
            return Failure(loose.From is null
                ? string.Format(UiText.Get("Grid.Ai.PickTable", "Name one of the tables after FROM: {0}"), string.Join(", ", schema.Tables.Select(t => t.Name)))
                : string.Format(UiText.Get("Grid.Ai.NoTable", "There is no table called '{0}'"), loose.From));
        }

        try
        {
            GridQueryPlan plan = GridQuery.Parse(query, [.. table.Columns.Select(column => column.Name)]);
            return new GridDraft(query, plan, table, null);
        }
        catch (GridQueryException exception)
        {
            return Failure(exception.Message);
        }
    }

    /// <summary>
    /// The query in a model's answer: a fenced block's contents if it has one, without a leading "Query:" or "SQL:" label or a
    /// trailing semicolon, and only its first line. Models add commentary around the query; the commentary is dropped.
    /// </summary>
    internal static string Clean(string answer)
    {
        string text = answer.Trim();
        int fence = text.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            text = text[(fence + 3)..];
            int lineEnd = text.IndexOf('\n');
            text = lineEnd >= 0 ? text[(lineEnd + 1)..] : string.Empty;
            int close = text.IndexOf("```", StringComparison.Ordinal);
            if (close >= 0)
            {
                text = text[..close];
            }
        }

        string line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
        foreach (string label in new[] { "Query:", "SQL:" })
        {
            if (line.StartsWith(label, StringComparison.OrdinalIgnoreCase))
            {
                line = line[label.Length..].Trim();
            }
        }

        return line.TrimEnd(';').Trim().Trim('`').Trim();
    }

    private static GridDraft Failure(string error) => new(null, null, null, error);
}

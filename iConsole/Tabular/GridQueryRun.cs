namespace iConsole.Tabular;

/// <summary>What a query over rows in memory gave: the result, or the message of the query that was refused.</summary>
public sealed record GridRunOutcome(GridQueryResult? Result, string? Error);

/// <summary>
/// Runs a query over rows a viewer already holds, on a background task, and keeps only the newest one: an answer overtaken by
/// a later query, or cancelled, is never taken. The viewer polls <see cref="TakeOutcome"/> from its idle pass.
/// </summary>
public sealed class GridQueryRun
{
    private int _generation;
    private int _pendingGeneration;
    private Task<GridQueryResult>? _task;
    private CancellationTokenSource? _cancellation;

    /// <summary>The query being waited for, or null.</summary>
    public Task<GridQueryResult>? Task => _task;

    /// <summary>Starts <paramref name="text"/> over the rows. Returns at once; an earlier query still running is overtaken.</summary>
    public void Start(IReadOnlyList<string> names, IReadOnlyList<string[]> rows, string text)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;
        _pendingGeneration = ++_generation;
        _task = System.Threading.Tasks.Task.Run(() => GridQuery.Run(names, rows, text, token), token);
    }

    /// <summary>The outcome of the newest query, once. Null while it runs, and for a query that was overtaken or cancelled.</summary>
    public GridRunOutcome? TakeOutcome()
    {
        if (_task is not { IsCompleted: true } task)
        {
            return null;
        }

        _task = null;
        if (_pendingGeneration != _generation || task.IsCanceled)
        {
            return null;
        }

        if (task.Exception is { } failure)
        {
            return new GridRunOutcome(null, failure.InnerException?.Message ?? failure.Message);
        }

        return new GridRunOutcome(task.Result, null);
    }

    /// <summary>
    /// Runs <paramref name="text"/> over every row of a source: its whole window is read through the array backend, with no row
    /// numbers, and the query runs over that table. The source must be in memory: a read here is not cancellable.
    /// </summary>
    public static Task<GridQueryResult> RunOverAsync(IGridArraySource source, string text, CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.Run(() => GridArrayView.Open(source).QueryAsync(text, GridArrayView.DefaultChunkCells, int.MaxValue, cancellationToken), cancellationToken);

    /// <summary>Starts <paramref name="text"/> over a source, through <see cref="RunOverAsync"/>. An earlier query still running is cancelled.</summary>
    public void Start(IGridArraySource source, string text)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        _pendingGeneration = ++_generation;
        _task = RunOverAsync(source, text, _cancellation.Token);
    }

    /// <summary>
    /// Runs <paramref name="text"/> over every cell of an array, streamed in blocks (see <see cref="GridArrayView.QueryLongFormAsync"/>).
    /// The result says when the match cap cut it short.
    /// </summary>
    public void StartLongForm(GridArrayView view, string text, int maxMatches)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;
        _pendingGeneration = ++_generation;
        _task = System.Threading.Tasks.Task.Run(() => view.QueryLongFormAsync(text, GridArrayView.DefaultChunkCells, maxMatches, token), token);
    }

    /// <summary>
    /// Runs a query that carries an axis clause: it reads the window the clause names from the array (see <see cref="GridArrayView.QueryAsync"/>),
    /// cancellable like a long-form search.
    /// </summary>
    public void StartWindow(GridArrayView view, string text, int maxMatches)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;
        _pendingGeneration = ++_generation;
        _task = System.Threading.Tasks.Task.Run(() => view.QueryAsync(text, GridArrayView.DefaultChunkCells, maxMatches, token), token);
    }

    /// <summary>Drops the query in progress.</summary>
    public void Cancel()
    {
        _generation++;
        _task = null;
        _cancellation?.Cancel();
    }
}

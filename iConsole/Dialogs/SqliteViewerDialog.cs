using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iConsole.Sqlite;
using iConsole.Tabular;

namespace iConsole.Dialogs;

/// <summary>
/// The SQLite viewer: a database file's query result as a grid, or as aligned text (F4). The query line takes one read-only
/// SQL statement (see <see cref="ISqliteDatabaseReader"/>). The default query lists the schema.
///
/// <para>A query runs on a background task, and its result lands on the next idle pass. Only the newest query may land; a
/// query that is cancelled, by a newer one or by closing the dialog, lands nowhere and writes nothing to the status line.</para>
/// </summary>
public class SqliteViewerDialog : Dialog
{
    /// <summary>The most rows a query keeps; a larger result says so on the status line.</summary>
    public const int MaxRows = 10_000;

    /// <summary>The query shown when the dialog opens: the tables, views and indexes of the database.</summary>
    public const string DefaultQuery = "SELECT type, name, tbl_name, sql FROM sqlite_master ORDER BY type, name";

    private const string CloseActionView = " [X]";

    private readonly ISqliteDatabaseReader _reader;
    private readonly TextBox _queryBox;
    private readonly CsvGrid _grid;
    private readonly TextViewerControl _textViewerControl;
    private readonly Label _statusLabel;
    private SqlTable? _table;
    private bool _textMode;
    private bool _textDirty;
    private int _generation;
    private int _pendingGeneration;
    private CancellationTokenSource? _cancellation;
    private Task<SqlTable>? _query;
    private SqlQueryArray? _resultArray;
    private ResultHolder? _pendingHolder;
    private Task<GridQueryResult>? _gridQuery;
    private CancellationTokenSource? _gridCancellation;
    private int _gridGeneration;
    private int _pendingGridGeneration;

    /// <summary>Carries the array a query reads from its worker thread back to the idle pass, which takes it once the query has landed.</summary>
    private sealed class ResultHolder
    {
        public SqlQueryArray? Array { get; set; }
    }
    private readonly GridQueryDrafter _drafter;

    public SqliteViewerDialog(
        ISqliteDatabaseReader reader,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService,
        IGridQueryAssistant? queryAssistant = null)
        : base(nameof(SqliteViewerDialog), themeService)
    {
        _reader = reader;
        _drafter = new GridQueryDrafter(queryAssistant ?? new UnavailableGridQueryAssistant());
        // The query line is added first: a dialog gives the keyboard to its first focusable control, and the query is where a person starts.
        _queryBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("Sqlite.Query.Label", "Query"),
            Placeholder = UiText.Get("Sqlite.Query.Placeholder", "e.g. SELECT * FROM table LIMIT 20"),
        };
        _grid = new CsvGrid(themeService) { EmptyText = UiText.Get("Sqlite.Grid.Empty", "The query returned no rows") };
        _textViewerControl = new TextViewerControl(false, themeService);
        _statusLabel = new Label(string.Empty, themeService);
        AddControl(_queryBox);
        AddControl(_grid);
        AddControl(_textViewerControl);
        AddControl(_statusLabel);
    }

    /// <summary>The database file shown. Must be a local file.</summary>
    public Url? Url { get; set; }

    /// <summary>The newest result that has landed, or null while the first query runs or when none has.</summary>
    public SqlTable? Table => _table;

    /// <summary>The grid, for a test or a caller to read the rows on screen.</summary>
    public CsvGrid Grid => _grid;

    /// <summary>True while the text output is shown instead of the grid (F4 toggles).</summary>
    public bool IsTextMode => _textMode;

    /// <summary>The query in progress, or null when none is running.</summary>
    public Task<SqlTable>? QueryTask => _query;

    /// <summary>What the status line says: the row count, the query's error, or why the file could not be shown.</summary>
    public string Status => _statusLabel.Text;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        if (Url is null)
        {
            _statusLabel.Text = UiText.Get("Viewer.NothingToShow", "Nothing to show");
            return;
        }

        _queryBox.Text = DefaultQuery;
        Title = TitleText();
        StartQuery(DefaultQuery);
    }

    /// <summary>The text in the query line.</summary>
    public string QueryText => _queryBox.Text;

    /// <summary>Replaces the text in the query line, as typing would.</summary>
    public void SetQueryText(string text) => _queryBox.Text = text;

    /// <summary>The grid query over the result being waited for, or null.</summary>
    public Task<GridQueryResult>? GridQueryTask => _gridQuery;

    /// <summary>The AI draft being waited for, or null.</summary>
    public Task<string>? DraftTask => _drafter.Request;

    /// <summary>
    /// Asks the AI model for a query for the question in the query line. The tables and their columns are read first, so the model
    /// names only what the database has; the draft lands in the line as the SQL it means, and runs on Enter.
    /// </summary>
    public void StartDraft()
    {
        if (Url is null)
        {
            return;
        }

        _drafter.Start(_queryBox.Text, LoadSchemaAsync(Url.Path));
        _statusLabel.Text = UiText.Get("Grid.Ai.Asking", "Asking the AI model…");
    }

    /// <summary>The tables and views of the file with their column names, read through the same read-only reader as a query.</summary>
    private async Task<GridSchema> LoadSchemaAsync(string path)
    {
        // The reader opens the file, so the whole call runs on a worker thread.
        return await Task.Run(async () =>
        {
            SqlTable names = await _reader.QueryAsync(path, "SELECT name FROM sqlite_master WHERE type IN ('table', 'view') ORDER BY name", 500, CancellationToken.None);
            List<GridTableSchema> tables = [];
            foreach (string[] row in names.Rows)
            {
                // LIMIT 0 reads the columns and no rows.
                SqlTable columns = await _reader.QueryAsync(path, GridQuery.ToSql(new GridQueryPlan(null, null, null, null, false, 0), row[0]), 0, CancellationToken.None);
                tables.Add(new GridTableSchema(row[0], [.. columns.Headers.Select(header => new GridColumnSchema(header))]));
            }

            return new GridSchema("SQLite database", tables);
        });
    }

    /// <summary>Starts a query over the database. It returns at once; the result lands on a later idle pass.</summary>
    public void StartQuery(string sql)
    {
        if (Url is null)
        {
            return;
        }

        Retire(_cancellation, _query);
        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;
        _pendingGeneration = ++_generation;
        string path = Url.Path;
        // The reader opens the file, so the whole call runs on a worker thread, never on the key or render path.
        _pendingHolder = new ResultHolder();
        ResultHolder holder = _pendingHolder;
        _query = Task.Run(async () => await QueryWindowAsync(path, sql, token, holder));
    }

    /// <summary>
    /// Runs a statement and returns the window the grid shows. A <c>SELECT</c> or <c>WITH</c> is read through the array backend, one
    /// block at a time; any other statement (such as <c>PRAGMA</c>) cannot be wrapped as a subquery and keeps the direct path.
    /// </summary>
    private async Task<SqlTable> QueryWindowAsync(string path, string sql, CancellationToken token, ResultHolder holder)
    {
        if (!IsRowStatement(sql))
        {
            return await _reader.QueryAsync(path, sql, MaxRows, token);
        }

        var array = new SqlQueryArray(_reader, path, sql);
        await array.InitializeAsync(token);
        holder.Array = array;
        GridTable window = await GridArrayView.Open(array).MaterializeAsync(MaxRows, int.MaxValue, int.MaxValue, token, withRowNumbers: false);
        return new SqlTable(window.Headers, window.Rows, window.RowsTruncated);
    }

    /// <summary>Whether the statement starts with <c>SELECT</c> or <c>WITH</c>: the statements that return rows and can be wrapped.</summary>
    private static bool IsRowStatement(string sql)
    {
        string word = sql.TrimStart().Split([' ', '\t', '\r', '\n', '('], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return string.Equals(word, "SELECT", StringComparison.OrdinalIgnoreCase) || string.Equals(word, "WITH", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Puts a finished query on screen. A stale or cancelled query is dropped without a word; a failed one keeps the rows on screen.</summary>
    /// <summary>
    /// Runs a grid query over the result on screen, through the array's single-statement path. A line that starts with <c>:</c> is this;
    /// any other line is raw SQL, as before. The result lands on the next idle pass; an older one is dropped.
    /// </summary>
    private void StartGridQuery(string text)
    {
        if (_resultArray is not { } array)
        {
            return;
        }

        _gridCancellation?.Cancel();
        _gridCancellation?.Dispose();
        _gridCancellation = new CancellationTokenSource();
        CancellationToken token = _gridCancellation.Token;
        _pendingGridGeneration = ++_gridGeneration;
        _gridQuery = Task.Run(() => array.QueryAsync(text, MaxRows, token), token);
    }

    public override void OnIdle()
    {
        // The newest query has finished without a failure: the result on screen is its result, and ':' reads from its array.
        if (_query is { IsCompleted: true, IsFaulted: false, IsCanceled: false } && _pendingHolder is { } landed && _pendingGeneration == _generation)
        {
            _resultArray = landed.Array;
        }

        if (_gridQuery is { IsCompleted: true } gridQuery && _pendingGridGeneration == _gridGeneration)
        {
            _gridQuery = null;
            if (gridQuery.Exception is { } failure)
            {
                Exception inner = failure.InnerException ?? failure;
                _statusLabel.Text = inner is OperationCanceledException ? string.Empty : inner.Message;
            }
            else if (!gridQuery.IsCanceled)
            {
                GridQueryResult result = gridQuery.Result;
                _grid.SetData(result.Headers, result.Rows);
                _statusLabel.Text = string.Format(UiText.Get("Sqlite.Status.Matched", "{0:N0} matching row(s)"), result.Matched);
            }

            StateHasChanged();
        }

        if (_drafter.TakeDraft() is { } draft)
        {
            ApplyDraft(draft);
        }

        if (_query is { IsCompleted: true } query && _pendingGeneration == _generation)
        {
            _query = null;
            // The newest query has finished, so its source is no longer needed.
            _cancellation?.Dispose();
            _cancellation = null;
            if (query.IsCanceled)
            {
                base.OnIdle();
                return;
            }

            if (query.Exception is { } failure)
            {
                Exception inner = failure.InnerException ?? failure;
                if (inner is OperationCanceledException)
                {
                    base.OnIdle();
                    return;
                }

                _statusLabel.Text = inner is SqlQueryException
                    ? inner.Message
                    : string.Format(UiText.Get("Sqlite.Error.Unexpected", "The query failed: {0}"), inner.Message);
            }
            else
            {
                SqlTable table = query.Result;
                _table = table;
                _textDirty = true;
                _grid.SetData(table.Headers, table.Rows);
                _statusLabel.Text = StatusFor(table);
            }

            StateHasChanged();
        }

        base.OnIdle();
    }

    /// <summary>Puts an AI draft in the query line as its SQL, or says why it cannot be used. The line runs only when the user presses Enter.</summary>
    private void ApplyDraft(GridDraft draft)
    {
        if (draft.Plan is { } plan && draft.Table is { } table)
        {
            _queryBox.Text = GridQuery.ToSql(plan, table.Name);
            _statusLabel.Text = UiText.Get("Grid.Ai.Ready", "Draft in the query line: press Enter to run it");
        }
        else
        {
            _statusLabel.Text = draft.Error ?? string.Empty;
        }

        StateHasChanged();
    }

    private string StatusFor(SqlTable table)
    {
        string status = string.Format(UiText.Get("Sqlite.Status.Rows", "{0:N0} row(s), {1} column(s)"), table.Rows.Count, table.Headers.Count);
        if (table.Truncated)
        {
            status += string.Format(UiText.Get("Sqlite.Status.Truncated", "; only the first {0:N0} rows are shown"), MaxRows);
        }

        return status;
    }

    /// <summary>Switches between the grid and the text output. The text is built from the newest result when it is first shown.</summary>
    public void ToggleTextMode()
    {
        if (!_textMode)
        {
            if (_textDirty && _table is not null)
            {
                _textViewerControl.TextModel.Lines.Clear();
                foreach (string line in SqlTableText.Format(_table))
                {
                    _textViewerControl.TextModel.Lines.Add(line);
                }

                _textDirty = false;
            }

            SetMode(textMode: true);
        }
        else
        {
            SetMode(textMode: false);
        }
    }

    private void SetMode(bool textMode)
    {
        _textMode = textMode;
        _queryBox.IsVisible = !textMode;
        _grid.IsVisible = !textMode;
        _textViewerControl.IsVisible = textMode;
        if (textMode)
        {
            _textViewerControl.SetFocus();
        }
        else
        {
            _queryBox.SetFocus();
        }

        Title = TitleText();
        StateHasChanged();
    }

    private string TitleText() =>
        (Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length)) ?? UiText.Get("Sqlite.Title", "SQLite"))
        + (_textMode
            ? UiText.Get("Sqlite.Title.TextMode", " [TEXT]")
            : UiText.Get("Sqlite.Title.Hint", "  F4:Text  Enter:query  Tab:grid"));

    /// <summary>Cancels the running query and drops its result, so nothing lands on a dialog that is gone.</summary>
    public override void Close()
    {
        _drafter.Cancel();
        _gridCancellation?.Cancel();
        _gridGeneration++;
        _pendingGeneration = ++_generation;
        Retire(_cancellation, _query);
        _cancellation = null;
        _query = null;
        base.Close();
    }

    /// <summary>
    /// Cancels a query that is being replaced or dropped, and disposes its source only once the query has finished: the
    /// running reader still holds the token until then.
    /// </summary>
    private static void Retire(CancellationTokenSource? source, Task? query)
    {
        if (source is null)
        {
            return;
        }

        source.Cancel();
        (query ?? Task.CompletedTask).ContinueWith(_ => source.Dispose(), TaskScheduler.Default);
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        // ShowAsync makes every child visible; the text view is on demand.
        _textMode = false;
        _textViewerControl.IsVisible = false;
        _queryBox.SetFocus();
    }

    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton(UiText.Get("Common.Close", "Close"), CloseActionView, new CloseSelfCommand(this), ThemeService));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType != KeyPressType.Down)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt && !_textMode)
        {
            if (GridQueryDrafter.IsQuestion(_queryBox.Text))
            {
                StartDraft();
            }
            else if (_queryBox.Text.TrimStart().StartsWith(':') && _resultArray is not null)
            {
                StartGridQuery(_queryBox.Text.TrimStart()[1..].Trim());
            }
            else
            {
                StartQuery(_queryBox.Text);
            }

            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.F4 && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            ToggleTextMode();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>The keys this dialog adds to the grid's, for the help screen.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", UiText.Get("Sqlite.Hint.RunQuery", "Run the query"));
        yield return (":", UiText.Get("Sqlite.Hint.GridQuery", "Filter the result: start the line with : and a grid query"));
        yield return ("?", UiText.Get("Grid.Hint.Ask", "Ask the AI model: type ? and a question, then Enter drafts the query"));
        yield return ("F4", UiText.Get("Viewer.HintToggleText", "Show the text output, or the grid again"));
    }

    private sealed class CloseSelfCommand(SqliteViewerDialog owner) : Command
    {
        public override Task ExecuteAsync(object? arg)
        {
            owner.Close();
            return Task.CompletedTask;
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            ViewSize = Parent.ViewSize;
            Position = new Position(0, 0);
            Title = TitleText();
        }

        // Inside the border: the query line, the grid or text, and the status line above the bottom border.
        int width = Math.Max(0, (int)ViewSize.Width - 2);
        int gridHeight = Math.Max(1, (int)ViewSize.Height - 5);
        int statusRow = Math.Max(0, (int)ViewSize.Height - 2);

        _queryBox.Position = new Position(1, 1);
        _queryBox.ViewSize = new Size(width, 1);

        _grid.Position = new Position(1, 2);
        _grid.ViewSize = new Size(width, gridHeight);

        _textViewerControl.Position = new Position(1, 1);
        _textViewerControl.ViewSize = new Size(width, Math.Max(1, (int)ViewSize.Height - 3));

        _statusLabel.Position = new Position(1, statusRow);
        _statusLabel.ViewSize = new Size(width, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}

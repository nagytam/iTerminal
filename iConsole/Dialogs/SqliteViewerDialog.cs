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

    public SqliteViewerDialog(
        ISqliteDatabaseReader reader,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(SqliteViewerDialog), themeService)
    {
        _reader = reader;
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
        _query = Task.Run(async () => await _reader.QueryAsync(path, sql, MaxRows, token));
    }

    /// <summary>Puts a finished query on screen. A stale or cancelled query is dropped without a word; a failed one keeps the rows on screen.</summary>
    public override void OnIdle()
    {
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
            StartQuery(_queryBox.Text);
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

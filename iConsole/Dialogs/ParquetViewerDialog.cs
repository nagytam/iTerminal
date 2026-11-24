using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Parquet;
using iConsole.Services;
using iConsole.Tabular;

namespace iConsole.Dialogs;

/// <summary>
/// The Parquet viewer: the first rows of a file as a grid, with the column names and types in the headers. A query line above
/// the grid takes the same query language as the CSV and SQLite viewers, over the rows shown; a line that starts with <c>?</c>
/// asks the AI model for a query, which lands in the line and runs on Enter. The status line gives the row and column counts and
/// says which axis was cut. Esc closes it.
///
/// <para>The file is read on a background task, never on a key or render path, and the result lands on the next idle pass.
/// A read that is replaced, or cancelled by closing the dialog, lands nowhere. A read that fails keeps the grid on screen and
/// says why on the status line.</para>
/// </summary>
public class ParquetViewerDialog : Dialog
{
    /// <summary>The most rows the grid keeps; a larger file says so on the status line.</summary>
    public const int MaxRows = 10_000;

    /// <summary>The most top-level columns the grid keeps.</summary>
    public const int MaxColumns = 100;

    /// <summary>The most cells the grid keeps; the rows are cut first to honour it.</summary>
    public const int MaxCells = 100_000;

    private const string CloseActionView = " [X]";

    private readonly IParquetFileReader _reader;
    private readonly TextBox _queryBox;
    private readonly CsvGrid _grid;
    private readonly Label _statusLabel;
    private readonly GridQueryRun _queryRun = new();
    private readonly GridQueryDrafter _drafter;
    private CancellationTokenSource? _cancellation;
    private readonly GridQueryCompletionModel _completion;
    private Task<ParquetPreview>? _read;
    private ParquetPreview? _preview;
    private int? _matched;
    private string _queryError = string.Empty;
    private string _statusNote = string.Empty;

    public ParquetViewerDialog(
        IParquetFileReader reader,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService,
        IGridQueryAssistant? queryAssistant = null)
        : base(nameof(ParquetViewerDialog), themeService)
    {
        _reader = reader;
        _drafter = new GridQueryDrafter(queryAssistant ?? new UnavailableGridQueryAssistant());
        // The query line is added first: the dialog gives the keyboard to its first focusable control, and the query is where a person starts.
        _queryBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("Csv.Query.Label", "Query"),
            Placeholder = UiText.Get("Grid.Query.Placeholder", "e.g. SELECT name WHERE amount > 10, or ? and a question"),
        };
        _completion = GridQueryCompletionModel.Attach(_queryBox);
        _grid = new CsvGrid(themeService) { EmptyText = UiText.Get("Parquet.Grid.Empty", "This file has no rows") };
        _statusLabel = new Label(string.Empty, themeService);
        AddControl(_queryBox);
        AddControl(_grid);
        AddControl(_statusLabel);
    }

    /// <summary>The file shown. Must be a local file.</summary>
    public Url? Url { get; set; }

    /// <summary>The grid, for a test or a caller to read the cells on screen.</summary>
    public CsvGrid Grid => _grid;

    /// <summary>What the status line says: the row and column counts, which axes were cut, a query's match count, an error, or why nothing is shown.</summary>
    public string Status => _statusLabel.Text;

    /// <summary>The text in the query line.</summary>
    public string QueryText => _queryBox.Text;

    /// <summary>The read in progress, or null when it has landed.</summary>
    public Task<ParquetPreview>? ReadTask => _read;

    /// <summary>The query being waited for, or null.</summary>
    public Task<GridQueryResult>? QueryTask => _queryRun.Task;

    /// <summary>The AI draft being waited for, or null.</summary>
    public Task<string>? DraftTask => _drafter.Request;

    /// <summary>Replaces the text in the query line, as typing would.</summary>
    public void SetQueryText(string text) => _queryBox.Text = text;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        // A panel reuses one dialog for every file, so nothing of the last file may survive an open.
        Retire(_cancellation, _read);
        _cancellation = null;
        _read = null;
        _preview = null;
        _queryRun.Cancel();
        _drafter.Cancel();
        _queryBox.Text = string.Empty;
        _queryError = string.Empty;
        _statusNote = string.Empty;
        _matched = null;
        _grid.SetData([], []);
        _statusLabel.Text = string.Empty;
        if (Url is null)
        {
            _statusLabel.Text = UiText.Get("Viewer.NothingToShow", "Nothing to show");
            return;
        }

        Title = TitleText();
        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;
        string path = Url.Path;
        // The reader opens the file, so the whole call runs on a worker thread.
        _read = Task.Run(() => _reader.ReadPreviewAsync(path, MaxRows, MaxColumns, MaxCells, token), CancellationToken.None);
    }

    /// <summary>Puts finished work on screen: a read, a query or an AI draft. A cancelled one is dropped without a word; a failed one keeps the grid.</summary>
    public override void OnIdle()
    {
        bool changed = false;
        if (_drafter.TakeDraft() is { } draft)
        {
            ApplyDraft(draft);
            changed = true;
        }

        if (_queryRun.TakeOutcome() is { } outcome)
        {
            ApplyOutcome(outcome);
            changed = true;
        }

        if (Land())
        {
            changed = true;
        }

        if (changed)
        {
            StateHasChanged();
        }

        base.OnIdle();
    }

    private bool Land()
    {
        if (_read is not { IsCompleted: true } read)
        {
            return false;
        }

        _read = null;
        _cancellation?.Dispose();
        _cancellation = null;
        if (Failure(read) is { } failure)
        {
            _statusLabel.Text = failure;
            return failure.Length > 0;
        }

        if (read.IsCanceled)
        {
            return false;
        }

        _preview = read.Result;
        _queryError = string.Empty;
        _statusNote = string.Empty;
        // A query typed while the file was being read applies to the rows that have just arrived.
        if (_queryBox.Text.Trim().Length > 0 && !GridQueryDrafter.IsQuestion(_queryBox.Text))
        {
            RunQuery(_queryBox.Text);
        }
        else
        {
            ShowAllRows();
        }

        return true;
    }

    /// <summary>Runs the query line over the rows shown. Enter does this; the grid changes on a later idle pass.</summary>
    private void RunQuery(string text)
    {
        if (_preview is not { } preview)
        {
            return;
        }

        _statusNote = string.Empty;
        _queryError = string.Empty;
        _queryRun.Start(new InMemoryGridArray(preview.Rows, preview.Names), text);
    }

    private void ShowAllRows()
    {
        if (_preview is not { } preview)
        {
            return;
        }

        _matched = null;
        _completion.SetColumns(preview.Headers);
        _grid.SetData(preview.Headers, preview.Rows);
        UpdateStatus();
    }

    private void ApplyOutcome(GridRunOutcome outcome)
    {
        if (outcome.Result is { } result)
        {
            _queryError = string.Empty;
            _matched = result.Matched;
            _grid.SetData(result.Headers, result.Rows);
        }
        else
        {
            // A query that was refused keeps the rows already on screen and says why.
            _queryError = outcome.Error ?? string.Empty;
        }

        UpdateStatus();
    }

    /// <summary>Asks the AI model for a query for the question in the query line. The draft lands in the line; Enter runs it.</summary>
    private void StartDraft()
    {
        if (_preview is not { } preview)
        {
            return;
        }

        IReadOnlyList<GridColumnSchema> columns = [.. preview.Names.Select((name, index) => new GridColumnSchema(name, TypeOf(preview.Headers[index], name)))];
        _drafter.Start(_queryBox.Text, GridSchema.ForSingleTable("Parquet file", columns, preview.TotalRows));
        _queryError = string.Empty;
        _statusNote = UiText.Get("Grid.Ai.Asking", "Asking the AI model…");
        UpdateStatus();
    }

    /// <summary>Puts an AI draft in the query line, or says why it cannot be used. The draft runs only when the user presses Enter.</summary>
    private void ApplyDraft(GridDraft draft)
    {
        if (draft.Query is { } query)
        {
            _queryBox.Text = query;
            _queryError = string.Empty;
            _statusNote = UiText.Get("Grid.Ai.Ready", "Draft in the query line: press Enter to run it");
        }
        else
        {
            _queryError = draft.Error ?? string.Empty;
            _statusNote = string.Empty;
        }

        UpdateStatus();
    }

    /// <summary>The type from a header written as <c>name (type)</c>, or empty when it is not in that form.</summary>
    private static string TypeOf(string header, string name)
    {
        string prefix = name + " (";
        return header.StartsWith(prefix, StringComparison.Ordinal) && header.EndsWith(')')
            ? header[prefix.Length..^1]
            : string.Empty;
    }

    /// <summary>The message of a failed task, an empty string for one that was only cancelled, or null for one that succeeded.</summary>
    private static string? Failure(Task task)
    {
        if (task.Exception is not { } exception)
        {
            return null;
        }

        Exception inner = exception.InnerException ?? exception;
        return inner switch
        {
            OperationCanceledException => string.Empty,
            ParquetReadException => inner.Message,
            _ => string.Format(UiText.Get("Parquet.Error.Unexpected", "The file could not be read: {0}"), inner.Message),
        };
    }

    private void UpdateStatus()
    {
        if (_queryError.Length > 0)
        {
            _statusLabel.Text = _queryError;
            return;
        }

        if (_statusNote.Length > 0)
        {
            _statusLabel.Text = _statusNote;
            return;
        }

        if (_preview is not { } preview)
        {
            return;
        }

        string status = StatusFor(preview);
        if (_matched is { } matched)
        {
            status += string.Format(UiText.Get("Grid.Status.Matched", "; {0:N0} of the {1:N0} shown row(s) match"), matched, preview.Rows.Count);
        }

        _statusLabel.Text = status;
    }

    private static string StatusFor(ParquetPreview preview)
    {
        string status = string.Format(UiText.Get("Parquet.Status.Summary", "{0:N0} rows, {1:N0} columns"), preview.TotalRows, preview.TotalColumns);
        if (preview.RowsTruncated)
        {
            status += string.Format(UiText.Get("Parquet.Status.RowsCut", "; only the first {0:N0} rows are shown"), preview.Rows.Count);
        }

        if (preview.ColumnsTruncated)
        {
            status += string.Format(UiText.Get("Parquet.Status.ColumnsCut", "; only the first {0:N0} columns are shown"), preview.Headers.Count);
        }

        return status;
    }

    private string TitleText() =>
        Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length)) ?? UiText.Get("Parquet.Title", "Parquet");

    /// <summary>Cancels a read that is still running, so nothing lands on a dialog that is gone.</summary>
    public override void Close()
    {
        Retire(_cancellation, _read);
        _cancellation = null;
        _read = null;
        _queryRun.Cancel();
        _drafter.Cancel();
        base.Close();
    }

    /// <summary>
    /// Cancels a read that is being replaced or dropped, and disposes its source only once the read has finished: the
    /// running reader still holds the token until then.
    /// </summary>
    private static void Retire(CancellationTokenSource? source, Task? read)
    {
        if (source is null)
        {
            return;
        }

        source.Cancel();
        (read ?? Task.CompletedTask).ContinueWith(_ => source.Dispose(), TaskScheduler.Default);
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        _queryBox.SetFocus();
    }

    /// <summary>Enter runs the query line, or drafts a query from a line that starts with <c>?</c>. Other keys are the grid's.</summary>
    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.Enter
            && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt
            && _queryBox.IsFocused && _preview is not null)
        {
            if (GridQueryDrafter.IsQuestion(_queryBox.Text))
            {
                StartDraft();
            }
            else if (_queryBox.Text.Trim().Length == 0)
            {
                _queryRun.Cancel();
                _queryError = string.Empty;
                _statusNote = string.Empty;
                ShowAllRows();
            }
            else
            {
                RunQuery(_queryBox.Text);
            }

            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>The keys this dialog adds to the grid's, for the help screen.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", UiText.Get("Grid.Hint.RunQuery", "Run the query over the rows shown"));
        foreach ((string Key, string Description) hint in GridQueryCompletionModel.Hints())
        {
            yield return hint;
        }

        yield return ("?", UiText.Get("Grid.Hint.Ask", "Ask the AI model: type ? and a question, then Enter drafts the query"));
    }

    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton(UiText.Get("Common.Close", "Close"), CloseActionView, new CloseSelfCommand(this), ThemeService));
    }

    private sealed class CloseSelfCommand(ParquetViewerDialog owner) : Command
    {
        public override Task ExecuteAsync(object? args)
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

        // Inside the border: the query line, the grid, and the status line above the bottom border.
        int width = Math.Max(0, (int)ViewSize.Width - 2);
        int gridHeight = Math.Max(1, (int)ViewSize.Height - 4);

        _queryBox.Position = new Position(1, 1);
        _queryBox.ViewSize = new Size(width, 1);

        _grid.Position = new Position(1, 2);
        _grid.ViewSize = new Size(width, gridHeight);

        _statusLabel.Position = new Position(1, Math.Max(0, (int)ViewSize.Height - 2));
        _statusLabel.ViewSize = new Size(width, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}

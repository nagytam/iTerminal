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

namespace iConsole.Dialogs;

/// <summary>
/// The Parquet viewer: the first rows of a file as a grid, with the column names and types in the headers. The status line
/// gives the row and column counts and says which axis was cut. Read-only; Esc closes it.
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
    private readonly CsvGrid _grid;
    private readonly Label _statusLabel;
    private CancellationTokenSource? _cancellation;
    private Task<ParquetPreview>? _read;

    public ParquetViewerDialog(IParquetFileReader reader, IThemeService themeService)
        : base(nameof(ParquetViewerDialog), themeService)
    {
        _reader = reader;
        _grid = new CsvGrid(themeService) { EmptyText = UiText.Get("Parquet.Grid.Empty", "This file has no rows") };
        _statusLabel = new Label(string.Empty, themeService);
        AddControl(_grid);
        AddControl(_statusLabel);
    }

    /// <summary>The file shown. Must be a local file.</summary>
    public Url? Url { get; set; }

    /// <summary>The grid, for a test or a caller to read the cells on screen.</summary>
    public CsvGrid Grid => _grid;

    /// <summary>What the status line says: the row and column counts, which axes were cut, an error, or why nothing is shown.</summary>
    public string Status => _statusLabel.Text;

    /// <summary>The read in progress, or null when it has landed.</summary>
    public Task<ParquetPreview>? ReadTask => _read;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        // A panel reuses one dialog for every file, so nothing of the last file may survive an open.
        Retire(_cancellation, _read);
        _cancellation = null;
        _read = null;
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
        _read = Task.Run(() => _reader.ReadPreviewAsync(path, MaxRows, MaxColumns, MaxCells, token));
    }

    /// <summary>Puts a finished read on screen. A cancelled one is dropped without a word; a failed one keeps the grid.</summary>
    public override void OnIdle()
    {
        if (Land())
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

        ParquetPreview preview = read.Result;
        _grid.SetData(preview.Headers, preview.Rows);
        _statusLabel.Text = StatusFor(preview);
        return true;
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
        _grid.SetFocus();
    }

    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton(UiText.Get("Common.Close", "Close"), CloseActionView, new CloseSelfCommand(this), ThemeService));
    }

    private sealed class CloseSelfCommand(ParquetViewerDialog owner) : Command
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

        // Inside the border: the grid, and the status line above the bottom border.
        int width = Math.Max(0, (int)ViewSize.Width - 2);
        int gridHeight = Math.Max(1, (int)ViewSize.Height - 3);

        _grid.Position = new Position(1, 1);
        _grid.ViewSize = new Size(width, gridHeight);

        _statusLabel.Position = new Position(1, Math.Max(0, (int)ViewSize.Height - 2));
        _statusLabel.ViewSize = new Size(width, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}

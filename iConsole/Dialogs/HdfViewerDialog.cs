using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Hdf;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Dialogs;

/// <summary>
/// The HDF5 viewer: a picker for the datasets of a file, and the chosen dataset as a grid. A dataset of three or more
/// dimensions shows one 2-D slice, the last two dimensions as rows and columns and the leading ones fixed by index; the
/// index of the active leading dimension steps with <c>+</c> and <c>-</c> (or Ctrl+Up and Ctrl+Down), and <c>[</c> and <c>]</c>
/// (Ctrl+Left and Ctrl+Right) choose which leading dimension is active.
///
/// <para>The file is read on background tasks, never on a key or render path, and a result lands on the next idle pass.
/// Only the newest slice may land: a slice that is replaced by a newer one, or cancelled by closing the dialog, lands nowhere
/// and writes nothing to the status line. A read that fails keeps the grid on screen and says why on the status line.</para>
/// </summary>
public class HdfViewerDialog : Dialog
{
    /// <summary>The most datasets the picker lists; a larger file says so on the status line.</summary>
    public const int MaxDatasets = 5_000;

    /// <summary>The most rows, columns and cells one slice keeps; a larger one says which axis was cut.</summary>
    public const int MaxRows = 10_000;

    public const int MaxColumns = 100;
    public const int MaxCells = 100_000;

    private const string CloseActionView = " [X]";

    private readonly IHdfFileReader _reader;
    private readonly ComboBox<HdfDatasetInfo> _picker;
    private readonly CsvGrid _grid;
    private readonly Label _sliceLabel;
    private readonly Label _statusLabel;
    private HdfDatasetInfo? _current;
    private long[] _indices = [];
    private int _activeDimension;
    private bool _datasetsTruncated;
    private int _generation;
    private int _pendingGeneration;
    private CancellationTokenSource? _listCancellation;
    private Task<HdfDatasetList>? _list;
    private CancellationTokenSource? _sliceCancellation;
    private Task<HdfSlice>? _slice;

    public HdfViewerDialog(IHdfFileReader reader, IThemeService themeService)
        : base(nameof(HdfViewerDialog), themeService)
    {
        _reader = reader;
        // The picker is added first: a dialog gives the keyboard to its first focusable control, and the dataset is where a person starts.
        _picker = new ComboBox<HdfDatasetInfo>(themeService)
        {
            Label = UiText.Get("Hdf.Picker.Label", "Dataset"),
            DisplaySelector = dataset => dataset.Path,
        };
        _picker.SelectionChanged += (_, _) => SelectCurrent();
        _grid = new CsvGrid(themeService) { EmptyText = UiText.Get("Hdf.Grid.Empty", "This dataset has no values") };
        _sliceLabel = new Label(string.Empty, themeService);
        _statusLabel = new Label(string.Empty, themeService);
        AddControl(_picker);
        AddControl(_grid);
        AddControl(_sliceLabel);
        AddControl(_statusLabel);
    }

    /// <summary>The file shown. Must be a local file.</summary>
    public Url? Url { get; set; }

    /// <summary>The dataset picker, for a test or a caller.</summary>
    public ComboBox<HdfDatasetInfo> Picker => _picker;

    /// <summary>The grid, for a test or a caller to read the cells on screen.</summary>
    public CsvGrid Grid => _grid;

    /// <summary>The dataset on screen, or null before the list has landed or when the file has none.</summary>
    public HdfDatasetInfo? CurrentDataset => _current;

    /// <summary>The index of each leading dimension of the slice on screen; empty for a dataset of two dimensions or fewer.</summary>
    public IReadOnlyList<long> LeadingIndices => _indices;

    /// <summary>Which leading dimension <c>+</c> and <c>-</c> step.</summary>
    public int ActiveDimension => _activeDimension;

    /// <summary>The slice read in progress, or null when none is running.</summary>
    public Task<HdfSlice>? SliceTask => _slice;

    /// <summary>The dataset listing in progress, or null when it has landed.</summary>
    public Task<HdfDatasetList>? ListTask => _list;

    /// <summary>What the status line says: the dataset and its shape, which axes were cut, an error, or why nothing is shown.</summary>
    public string Status => _statusLabel.Text;

    /// <summary>The line under the grid that names the slice and the keys that move it; empty for a dataset of two dimensions or fewer.</summary>
    public string SliceText => _sliceLabel.Text;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        // ShowAsync runs this on every open and a panel reuses one dialog for every file, so nothing of the last file may
        // survive: the picker would keep its old selected index and open the new file on the wrong dataset.
        _current = null;
        _indices = [];
        _activeDimension = 0;
        _datasetsTruncated = false;
        _picker.SetItems([]);
        _grid.SetData([], []);
        _sliceLabel.Text = string.Empty;
        _statusLabel.Text = string.Empty;
        if (Url is null)
        {
            _statusLabel.Text = UiText.Get("Viewer.NothingToShow", "Nothing to show");
            return;
        }

        Title = TitleText();
        _listCancellation = new CancellationTokenSource();
        CancellationToken token = _listCancellation.Token;
        string path = Url.Path;
        // The reader opens the file, so the whole call runs on a worker thread.
        _list = Task.Run(async () => await _reader.ListDatasetsAsync(path, MaxDatasets, token));
    }

    /// <summary>Puts a finished listing or slice on screen. A stale or cancelled one is dropped without a word; a failed one keeps the grid.</summary>
    public override void OnIdle()
    {
        bool changed = LandList();
        changed |= LandSlice();
        if (changed)
        {
            StateHasChanged();
        }

        base.OnIdle();
    }

    private bool LandList()
    {
        if (_list is not { IsCompleted: true } list)
        {
            return false;
        }

        _list = null;
        _listCancellation?.Dispose();
        _listCancellation = null;
        if (Failure(list) is { } failure)
        {
            _statusLabel.Text = failure;
            return failure.Length > 0;
        }

        if (list.IsCanceled)
        {
            return false;
        }

        HdfDatasetList datasets = list.Result;
        _datasetsTruncated = datasets.Truncated;
        _picker.SetItems(datasets.Datasets);
        if (datasets.Datasets.Count == 0)
        {
            _statusLabel.Text = UiText.Get("Hdf.Status.NoDatasets", "This file has no datasets");
            return true;
        }

        SelectCurrent();
        return true;
    }

    private bool LandSlice()
    {
        if (_slice is not { IsCompleted: true } slice || _pendingGeneration != _generation)
        {
            return false;
        }

        _slice = null;
        // The newest slice has finished, so its source is no longer needed.
        _sliceCancellation?.Dispose();
        _sliceCancellation = null;
        if (Failure(slice) is { } failure)
        {
            _statusLabel.Text = failure;
            return failure.Length > 0;
        }

        if (slice.IsCanceled || _current is null)
        {
            return false;
        }

        HdfSlice result = slice.Result;
        _grid.SetData(result.Headers, result.Rows);
        _statusLabel.Text = StatusFor(_current, result);
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
            HdfReadException => inner.Message,
            _ => string.Format(UiText.Get("Hdf.Error.Unexpected", "The file could not be read: {0}"), inner.Message),
        };
    }

    private string StatusFor(HdfDatasetInfo dataset, HdfSlice slice)
    {
        string shape = dataset.Shape.Length == 0 ? UiText.Get("Hdf.Shape.Scalar", "scalar") : string.Join(" × ", dataset.Shape);
        string status = $"{dataset.Path}  {dataset.TypeName} [{shape}]";
        if (slice.RowsTruncated)
        {
            status += string.Format(UiText.Get("Hdf.Status.RowsCut", "; only the first {0:N0} rows are shown"), slice.Rows.Count);
        }

        if (slice.ColumnsTruncated)
        {
            status += string.Format(UiText.Get("Hdf.Status.ColumnsCut", "; only the first {0:N0} columns are shown"), slice.Headers.Count - 1);
        }

        if (_datasetsTruncated)
        {
            status += string.Format(UiText.Get("Hdf.Status.DatasetsCut", "; the list holds only the first {0:N0} datasets"), MaxDatasets);
        }

        return status;
    }

    /// <summary>The picker moved to another dataset: show its first slice.</summary>
    private void SelectCurrent()
    {
        _current = _picker.SelectedItem;
        _indices = _current is null ? [] : new long[_current.LeadingDimensions];
        _activeDimension = 0;
        UpdateSliceText();
        StartSlice();
    }

    /// <summary>Starts reading the slice on screen. It returns at once; the result lands on a later idle pass.</summary>
    private void StartSlice()
    {
        if (Url is null || _current is null)
        {
            return;
        }

        Retire(_sliceCancellation, _slice);
        _sliceCancellation = new CancellationTokenSource();
        CancellationToken token = _sliceCancellation.Token;
        _pendingGeneration = ++_generation;
        string path = Url.Path;
        string dataset = _current.Path;
        long[] indices = [.. _indices];
        _slice = Task.Run(async () => await _reader.ReadSliceAsync(path, dataset, indices, MaxRows, MaxColumns, MaxCells, token));
    }

    /// <summary>Moves the active leading dimension's index by <paramref name="delta"/>, clamped to the dimension, and reads the new slice.</summary>
    public void StepIndex(int delta)
    {
        if (_current is null || _indices.Length == 0)
        {
            return;
        }

        long next = Math.Clamp(_indices[_activeDimension] + delta, 0, _current.Shape[_activeDimension] - 1);
        if (next == _indices[_activeDimension])
        {
            return;
        }

        _indices[_activeDimension] = next;
        UpdateSliceText();
        StartSlice();
    }

    /// <summary>Makes the previous or next leading dimension the active one, clamped to the leading dimensions.</summary>
    public void StepDimension(int delta)
    {
        if (_indices.Length == 0)
        {
            return;
        }

        int next = Math.Clamp(_activeDimension + delta, 0, _indices.Length - 1);
        if (next != _activeDimension)
        {
            _activeDimension = next;
            UpdateSliceText();
            StateHasChanged();
        }
    }

    private void UpdateSliceText()
    {
        if (_current is null || _indices.Length == 0)
        {
            _sliceLabel.Text = string.Empty;
            return;
        }

        string cut = string.Join(", ", _indices.Select((index, dimension) => dimension == _activeDimension ? $"<{index}>" : index.ToString())
            .Concat(["*", "*"]));
        _sliceLabel.Text = string.Format(
            UiText.Get("Hdf.Slice.Line", "Slice [{0}]  dimension {1} of {2}, index {3} of {4}   [ ] dimension   + - index"),
            cut,
            _activeDimension + 1,
            _indices.Length,
            _indices[_activeDimension] + 1,
            _current.Shape[_activeDimension]);
    }

    private string TitleText() =>
        (Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length)) ?? UiText.Get("Hdf.Title", "HDF5"))
        + UiText.Get("Hdf.Title.Hint", "  Tab:grid  [ ]:dimension  +/-:index");

    /// <summary>Cancels what is running and drops its result, so nothing lands on a dialog that is gone.</summary>
    public override void Close()
    {
        _pendingGeneration = ++_generation;
        Retire(_listCancellation, _list);
        Retire(_sliceCancellation, _slice);
        _listCancellation = null;
        _list = null;
        _sliceCancellation = null;
        _slice = null;
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
        _picker.SetFocus();
    }

    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton(UiText.Get("Common.Close", "Close"), CloseActionView, new CloseSelfCommand(this), ThemeService));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down && !_picker.IsOpen && _indices.Length > 0 && HandleSliceKey(keyEvent))
        {
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>
    /// The slice keys. The typed ones are matched on the character, not the key: <c>+</c> is Shift+= on some layouts, and
    /// <c>[</c> and <c>]</c> are AltGr chords on most non-US ones. Ctrl+arrows work on every layout.
    /// </summary>
    private bool HandleSliceKey(KeyEvent keyEvent)
    {
        if (keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.LeftArrow:
                    StepDimension(-1);
                    return true;
                case ConsoleKey.RightArrow:
                    StepDimension(1);
                    return true;
                case ConsoleKey.UpArrow:
                    StepIndex(1);
                    return true;
                case ConsoleKey.DownArrow:
                    StepIndex(-1);
                    return true;
            }

            return false;
        }

        if (!keyEvent.Control && !keyEvent.Alt)
        {
            if (keyEvent.ConsoleKey == ConsoleKey.Add)
            {
                StepIndex(1);
                return true;
            }

            if (keyEvent.ConsoleKey == ConsoleKey.Subtract)
            {
                StepIndex(-1);
                return true;
            }
        }

        if (!ConsoleKeyHelper.IsTypingKey(keyEvent))
        {
            return false;
        }

        switch (keyEvent.KeyChar)
        {
            case '+':
            case '=':
                StepIndex(1);
                return true;
            case '-':
                StepIndex(-1);
                return true;
            case '[':
                StepDimension(-1);
                return true;
            case ']':
                StepDimension(1);
                return true;
        }

        return false;
    }

    /// <summary>The keys this dialog adds to the picker's and the grid's, for the help screen.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("+ / -  (Ctrl+↑ / Ctrl+↓)", UiText.Get("Hdf.Hint.Index", "Step the index of the active dimension"));
        yield return ("[ / ]  (Ctrl+← / Ctrl+→)", UiText.Get("Hdf.Hint.Dimension", "Choose the active dimension"));
    }

    private sealed class CloseSelfCommand(HdfViewerDialog owner) : Command
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

        // Inside the border: the picker, the grid, the slice line and the status line above the bottom border.
        int width = Math.Max(0, (int)ViewSize.Width - 2);
        int gridHeight = Math.Max(1, (int)ViewSize.Height - 5);

        _picker.Position = new Position(1, 1);
        _picker.ViewSize = new Size(width, 1);

        _grid.Position = new Position(1, 2);
        _grid.ViewSize = new Size(width, gridHeight);

        _sliceLabel.Position = new Position(1, Math.Max(0, (int)ViewSize.Height - 3));
        _sliceLabel.ViewSize = new Size(width, 1);

        _statusLabel.Position = new Position(1, Math.Max(0, (int)ViewSize.Height - 2));
        _statusLabel.ViewSize = new Size(width, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}

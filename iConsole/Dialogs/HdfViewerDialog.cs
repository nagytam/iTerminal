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
using iConsole.Tabular;

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

    /// <summary>The most matching cells a FROM cells query keeps; the whole array is searched, but a larger result says so.</summary>
    public const int MaxMatches = 100_000;

    private const string CloseActionView = " [X]";

    private readonly IHdfFileReader _reader;
    private readonly ComboBox<HdfDatasetInfo> _picker;
    private readonly CsvGrid _grid;
    private readonly Label _sliceLabel;
    private readonly Label _statusLabel;
    private HdfDatasetInfo? _current;
    private GridArrayView? _view;
    /// <summary>The number of fixed axes: the axes a slice fixes by index, which the picker's dimension keys step through.</summary>
    private int LeadingCount => _view?.FixedAxes.Count ?? 0;
    private int _activeDimension;
    private bool _datasetsTruncated;
    private int _generation;
    private int _pendingGeneration;
    private CancellationTokenSource? _listCancellation;
    private Task<HdfDatasetList>? _list;
    private CancellationTokenSource? _sliceCancellation;
    private Task<GridTable>? _slice;
    private readonly TextBox _queryBox;
    private readonly GridQueryCompletionModel _completion;
    private readonly GridQueryRun _queryRun = new();
    private readonly GridQueryDrafter _drafter;
    private GridTable? _sliceData;
    private bool _longFormCut;
    private int? _matched;
    private string _queryError = string.Empty;
    private string _statusNote = string.Empty;

    public HdfViewerDialog(
        IHdfFileReader reader,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService,
        IGridQueryAssistant? queryAssistant = null)
        : base(nameof(HdfViewerDialog), themeService)
    {
        _reader = reader;
        _drafter = new GridQueryDrafter(queryAssistant ?? new UnavailableGridQueryAssistant());
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
        // The query line sits under the picker. It runs over the slice shown, and a line that starts with ? asks the AI model.
        _queryBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("Csv.Query.Label", "Query"),
            Placeholder = UiText.Get("Grid.Query.Placeholder", "e.g. SELECT * WHERE 0 > 10, or ? and a question"),
        };
        _completion = GridQueryCompletionModel.Attach(_queryBox);
        AddControl(_picker);
        AddControl(_queryBox);
        AddControl(_grid);
        AddControl(_sliceLabel);
        AddControl(_statusLabel);
    }

    /// <summary>The text in the query line.</summary>
    public string QueryText => _queryBox.Text;

    /// <summary>Replaces the text in the query line, as typing would.</summary>
    public void SetQueryText(string text) => _queryBox.Text = text;

    /// <summary>Gives the query line the keyboard, as Tab does; typed characters then go to the line and not to the slice keys.</summary>
    public void FocusQueryLine() => _queryBox.SetFocus();

    /// <summary>The query being waited for, or null.</summary>
    public Task<GridQueryResult>? QueryTask => _queryRun.Task;

    /// <summary>The AI draft being waited for, or null.</summary>
    public Task<string>? DraftTask => _drafter.Request;

    /// <summary>The file shown. Must be a local file.</summary>
    public Url? Url { get; set; }

    /// <summary>The dataset picker, for a test or a caller.</summary>
    public ComboBox<HdfDatasetInfo> Picker => _picker;

    /// <summary>The grid, for a test or a caller to read the cells on screen.</summary>
    public CsvGrid Grid => _grid;

    /// <summary>The dataset on screen, or null before the list has landed or when the file has none.</summary>
    public HdfDatasetInfo? CurrentDataset => _current;

    /// <summary>The index of each leading dimension of the slice on screen; empty for a dataset of two dimensions or fewer.</summary>
    public IReadOnlyList<long> LeadingIndices => _view is null ? [] : [.. _view.FixedAxes.Select(axis => _view.IndexOf(axis))];

    /// <summary>Which leading dimension <c>+</c> and <c>-</c> step.</summary>
    public int ActiveDimension => _activeDimension;

    /// <summary>The slice read in progress, or null when none is running.</summary>
    public Task<GridTable>? SliceTask => _slice;

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
        _view = null;
        _activeDimension = 0;
        _datasetsTruncated = false;
        _picker.SetItems([]);
        _grid.SetData([], []);
        _sliceLabel.Text = string.Empty;
        _statusLabel.Text = string.Empty;
        _sliceData = null;
        _queryRun.Cancel();
        _drafter.Cancel();
        _queryBox.Text = string.Empty;
        _queryError = string.Empty;
        _statusNote = string.Empty;
        _matched = null;
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

        changed |= LandList();
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

        _sliceData = slice.Result;
        _queryError = string.Empty;
        _statusNote = string.Empty;
        // A query in the line applies to the slice that has just arrived, so stepping an index keeps the query.
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

    /// <summary>Runs the query line over the slice shown. The grid changes on a later idle pass.</summary>
    private void RunQuery(string text)
    {
        if (_sliceData is not { } slice)
        {
            return;
        }

        _statusNote = string.Empty;
        _queryError = string.Empty;
        if (_view is not null && AsksForLongForm(text))
        {
            // FROM cells: the query runs over every cell of the array, one row each, with a column per axis.
            _queryRun.StartLongForm(_view, text, MaxMatches);
            return;
        }

        if (_view is not null && _view.AxisClauseOf(text) is not null)
        {
            // AT, ROWS or COLUMNS: the query names the window of the array it reads, which may differ from the slice on screen.
            _queryRun.StartWindow(_view, text, MaxMatches);
            return;
        }

        _queryRun.Start(slice.Headers, slice.Rows, text);
    }

    /// <summary>Whether a query names the long form (<c>FROM cells</c>), which runs over every cell of the array rather than the window.</summary>
    private bool AsksForLongForm(string text)
    {
        if (_view is null)
        {
            return false;
        }

        try
        {
            return string.Equals(GridQuery.Parse(text, _view.LongFormHeaders).From, "cells", StringComparison.OrdinalIgnoreCase);
        }
        catch (GridQueryException)
        {
            return false;
        }
    }

    private void ShowAllRows()
    {
        if (_sliceData is not { } slice)
        {
            return;
        }

        _matched = null;
        _longFormCut = false;
        _completion.SetColumns(slice.Headers);
        _grid.SetData(slice.Headers, slice.Rows);
        UpdateStatus();
    }

    private void ApplyOutcome(GridRunOutcome outcome)
    {
        if (outcome.Result is { } result)
        {
            _queryError = string.Empty;
            _matched = result.Matched;
            _longFormCut = result.Truncated;
            _grid.SetData(result.Headers, result.Rows);
        }
        else
        {
            // A query that was refused keeps the slice on screen and says why.
            _queryError = outcome.Error ?? string.Empty;
        }

        UpdateStatus();
    }

    /// <summary>Asks the AI model for a query for the question in the query line, over the columns of the slice shown.</summary>
    private void StartDraft()
    {
        if (_sliceData is not { } slice)
        {
            return;
        }

        IReadOnlyList<GridColumnSchema> columns = [.. slice.Headers.Select(header => new GridColumnSchema(header))];
        _drafter.Start(_queryBox.Text, GridSchema.ForSingleTable("HDF5 dataset slice", columns, slice.Rows.Count));
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

        if (_current is null || _sliceData is not { } slice)
        {
            return;
        }

        string status = StatusFor(_current, slice);
        if (_matched is { } matched)
        {
            status += string.Format(UiText.Get("Grid.Status.Matched", "; {0:N0} of the {1:N0} shown row(s) match"), matched, slice.Rows.Count);
        }

        if (_longFormCut)
        {
            status += string.Format(UiText.Get("Hdf.Status.LongFormCut", "; only the first {0:N0} matching cells are kept"), MaxMatches);
        }

        _statusLabel.Text = status;
    }

    private string StatusFor(HdfDatasetInfo dataset, GridTable slice)
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
        _view = _current is null || Url is null ? null : GridArrayView.Open(new HdfGridArray(_reader, Url.Path, _current));
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
        // The view is immutable, so the read keeps the axes it was started with even if a key moves them meanwhile.
        GridArrayView view = _view!;
        _slice = Task.Run(async () => await view.MaterializeAsync(MaxRows, MaxColumns, MaxCells, token));
    }

    /// <summary>Moves the active leading dimension's index by <paramref name="delta"/>, clamped to the dimension, and reads the new slice.</summary>
    public void StepIndex(int delta)
    {
        if (_view is null || LeadingCount == 0)
        {
            return;
        }

        int axis = _view.FixedAxes[_activeDimension];
        long before = _view.IndexOf(axis);
        _view = _view.Step(axis, delta);
        if (_view.IndexOf(axis) == before)
        {
            return;
        }

        UpdateSliceText();
        StartSlice();
    }

    /// <summary>Makes the previous or next leading dimension the active one, clamped to the leading dimensions.</summary>
    public void StepDimension(int delta)
    {
        if (LeadingCount == 0)
        {
            return;
        }

        int next = Math.Clamp(_activeDimension + delta, 0, LeadingCount - 1);
        if (next != _activeDimension)
        {
            _activeDimension = next;
            UpdateSliceText();
            StateHasChanged();
        }
    }

    private void UpdateSliceText()
    {
        if (_current is null || _view is null || LeadingCount == 0)
        {
            _sliceLabel.Text = string.Empty;
            return;
        }

        // The position as the view describes it: the fixed index of each axis, with the active one marked, and * for the shown axes.
        string[] parts = [.. Enumerable.Range(0, _view.Rank).Select(axis =>
        {
            if (axis == _view.RowAxis || axis == _view.ColumnAxis)
            {
                return "*";
            }

            return _view.FixedAxes.ToList().IndexOf(axis) == _activeDimension ? $"<{_view.IndexOf(axis)}>" : _view.IndexOf(axis).ToString();
        })];
        int active = _view.FixedAxes[_activeDimension];
        _sliceLabel.Text = string.Format(
            UiText.Get("Hdf.Slice.Line", "Slice [{0}]  dimension {1} of {2}, index {3} of {4}   [ ] dimension   + - index"),
            string.Join(", ", parts),
            _activeDimension + 1,
            LeadingCount,
            _view.IndexOf(active) + 1,
            _view.Shape[active]);
    }

    private string TitleText() =>
        (Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length)) ?? UiText.Get("Hdf.Title", "HDF5"))
        + UiText.Get("Hdf.Title.Hint", "  Tab:grid  [ ]:dimension  +/-:index");

    /// <summary>Cancels what is running and drops its result, so nothing lands on a dialog that is gone.</summary>
    public override void Close()
    {
        _queryRun.Cancel();
        _drafter.Cancel();
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
        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.Enter
            && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt
            && !_picker.IsOpen && _queryBox.IsFocused && _sliceData is not null)
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

        if (keyEvent.KeyPressType == KeyPressType.Down && !_picker.IsOpen && LeadingCount > 0 && HandleSliceKey(keyEvent))
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

        if (_queryBox.IsFocused)
        {
            // The query line takes typed characters: + and - are part of a number in a query. Ctrl+arrows still step.
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
            case 'T':
            case 't':
                // Swaps the rows and the columns: the same two axes, shown the other way round.
                if (_view is { Rank: >= 2 })
                {
                    _view = _view.Transpose();
                    UpdateSliceText();
                    StartSlice();
                }

                return true;
        }

        return false;
    }

    /// <summary>The keys this dialog adds to the picker's and the grid's, for the help screen.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("+ / -  (Ctrl+↑ / Ctrl+↓)", UiText.Get("Hdf.Hint.Index", "Step the index of the active dimension"));
        yield return ("[ / ]  (Ctrl+← / Ctrl+→)", UiText.Get("Hdf.Hint.Dimension", "Choose the active dimension"));
        yield return ("T", UiText.Get("Hdf.Hint.Transpose", "Swap the rows and the columns"));
        yield return ("FROM cells", UiText.Get("Hdf.Hint.LongForm", "Query every cell, with a column per axis (axis0, axis1, ...)"));
        yield return ("Enter", UiText.Get("Grid.Hint.RunQuery", "Run the query over the rows shown"));
        foreach ((string Key, string Description) hint in GridQueryCompletionModel.Hints())
        {
            yield return hint;
        }

        yield return ("?", UiText.Get("Grid.Hint.Ask", "Ask the AI model: type ? and a question, then Enter drafts the query"));
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
        int gridHeight = Math.Max(1, (int)ViewSize.Height - 6);

        _picker.Position = new Position(1, 1);
        _picker.ViewSize = new Size(width, 1);

        _queryBox.Position = new Position(1, 2);
        _queryBox.ViewSize = new Size(width, 1);

        _grid.Position = new Position(1, 3);
        _grid.ViewSize = new Size(width, gridHeight);

        _sliceLabel.Position = new Position(1, Math.Max(0, (int)ViewSize.Height - 3));
        _sliceLabel.ViewSize = new Size(width, 1);

        _statusLabel.Position = new Position(1, Math.Max(0, (int)ViewSize.Height - 2));
        _statusLabel.ViewSize = new Size(width, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}

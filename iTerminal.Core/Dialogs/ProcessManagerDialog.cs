using iConsole.Localization;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using System.Globalization;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Lists the running processes with the memory and processor time each uses, sampled about once a second while the dialog is
/// shown. The table sorts by a click on a header or from the keyboard, and the filter narrows it by name or id. Sampling runs on a
/// worker thread and its result is picked up from <see cref="OnIdle"/>, so the control tree is only touched on the UI thread.
/// The cursor stays on the same process from one sample to the next.
/// </summary>
public class ProcessManagerDialog : Dialog
{
    /// <summary>How often the processes are sampled while the dialog is open.</summary>
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private const int NameColumn = 0;
    private const int IdColumn = 1;
    private const int CpuColumn = 2;
    private const int MemoryColumn = 3;
    private const int ColumnCount = 4;
    private const int IdWidth = 7;
    private const int CpuWidth = 7;
    private const int MemoryWidth = 10;
    private const int ColumnGaps = 3;

    private readonly ProcessService _processService;
    private readonly ITimeProvider _time;
    private readonly ConcurrentObservableCollection<ProcessSample> _rows = [];
    private readonly Table<ProcessSample> _table;
    private readonly Label _summary;
    private readonly TextBox _textbox;
    private readonly CloseButton _closeButton;

    // Written by the sampling worker under _gate, read on the UI thread.
    private readonly Lock _gate = new();
    private IReadOnlyList<ProcessSample> _latest = [];
    private int _latestVersion;

    // UI thread only.
    private int _shownVersion;
    private bool _viewChanged;
    private long _lastSampleStarted = -1;
    private int _sortColumn = CpuColumn;
    private bool _sortDescending = true;
    private volatile bool _sampling;

    public ProcessManagerDialog(
        IBeepService beepService,
        ProcessService processService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService,
        ITimeProvider time)
        : base(nameof(ProcessManagerDialog), themeService)
    {
        _processService = processService ?? new ProcessService();
        _time = time;

        Title = UiText.Get("Dialog.ProcessManager", "Process manager");
        _textbox = new TextBox(beepService, dateTimeProviderService, themeService) { Label = UiText.Get("Field.Filter", "Filter"), Placeholder = UiText.Get("Field.ProcessNameOrId", "process name or id") };
        AddControl(_textbox);

        _table = new Table<ProcessSample>(_rows, themeService)
        {
            EmptyText = UiText.Get("Empty.Processes", " No process matches the filter."),
            SortColumn = _sortColumn,
            SortDescending = _sortDescending,
        };
        _table.SortRequested += (_, column) => SortBy(column);
        AddControl(_table);

        _summary = new Label(string.Empty, themeService);
        AddControl(_summary);

        // Subscribed once, here: the filter only marks the view as changed, and the next idle pass rebuilds the rows.
        _textbox.PropertyChanged += (_, _) => _viewChanged = true;
        _closeButton = new CloseButton(themeService);
        AddControl(_closeButton);
    }

    internal Table<ProcessSample> TableForTest => _table;
    internal TextBox FilterBoxForTest => _textbox;
    internal IReadOnlyList<ProcessSample> RowsForTest => [.. _rows.AsSnapshot()];
    internal int SortColumnForTest => _sortColumn;
    internal bool SortDescendingForTest => _sortDescending;

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(Parent.ViewSize * 0.8);
        }

        _textbox.Position = new Position(1, 1);
        _textbox.ViewSize = new Size(ViewSize.Width - 2, 1);
        _textbox.ScrollableModel.ViewSize = _textbox.ViewSize;

        // Rows 2 to Height-5 are the table, the line above the close button says how many are shown.
        _table.Position = new Position(1, 2);
        _table.ViewSize = new Size(ViewSize.Width - 2, ViewSize.Height - 5);
        _table.ScrollableModel.ViewSize = new Size(_table.ViewSize.Width, Math.Max(1, _table.ViewSize.Height - 1));
        ConfigureColumns();

        _summary.Position = new Position(1, ViewSize.Height - 3);
        _summary.ViewSize = new Size(ViewSize.Width - 2, 1);
        _closeButton.Position = new Position(1, ViewSize.Height - 2);
        _closeButton.ViewSize = new Size(ViewSize.Width - 2, 1);

        base.OnInitializeLayout();
    }

    /// <summary>
    /// The name takes the width that is left. Numbers are right-aligned, so each figure sits under the end of its header.
    /// </summary>
    private void ConfigureColumns()
    {
        int nameWidth = Math.Max(4, (int)_table.ViewSize.Width - IdWidth - CpuWidth - MemoryWidth - ColumnGaps);
        _table.Columns.Clear();
        _table.Columns.Add(new ColumnDefinition<ProcessSample>(UiText.Get("Process.Column.Name", "Name"), nameWidth, p => p.Name));
        _table.Columns.Add(new ColumnDefinition<ProcessSample>(UiText.Get("Process.Column.Pid", "PID"), IdWidth,
            p => p.Id.ToString(CultureInfo.InvariantCulture), TextAlignment.Right));
        _table.Columns.Add(new ColumnDefinition<ProcessSample>(UiText.Get("Process.Column.Cpu", "CPU"), CpuWidth,
            p => p.CpuPercent is { } cpu ? cpu.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "-", TextAlignment.Right));
        _table.Columns.Add(new ColumnDefinition<ProcessSample>(UiText.Get("Process.Column.Memory", "Memory"), MemoryWidth,
            p => p.WorkingSetBytes is { } bytes ? bytes.ToFileSize().Replace("  ", " ").Trim() : "-", TextAlignment.Right));
    }

    public override void OnIdle()
    {
        base.OnIdle();

        long now = _time.MonotonicMilliseconds;
        if (!_sampling && (_lastSampleStarted < 0 || now - _lastSampleStarted >= RefreshInterval.TotalMilliseconds))
        {
            StartSample(now);
        }

        int version;
        lock (_gate)
        {
            version = _latestVersion;
        }

        // Nothing is shown before the first sample lands, so the list does not flash "no process" while it waits.
        if (version > 0 && (version != _shownVersion || _viewChanged))
        {
            _shownVersion = version;
            _viewChanged = false;
            Rebuild();
        }
    }

    private void StartSample(long now)
    {
        _sampling = true;
        _lastSampleStarted = now;
        _ = Task.Run(() =>
        {
            try
            {
                IReadOnlyList<ProcessSample> samples = _processService.Sample();
                lock (_gate)
                {
                    _latest = samples;
                    _latestVersion++;
                }
            }
            finally
            {
                _sampling = false;
            }
        });
    }

    private void Rebuild()
    {
        IReadOnlyList<ProcessSample> latest;
        lock (_gate)
        {
            latest = _latest;
        }

        string filter = _textbox.Text;
        List<ProcessSample> shown = [.. Sorted(latest.Where(p => Matches(p, filter)))];

        // The cursor stays on the same process while it is listed; otherwise it keeps its row number, clamped.
        int? selectedId = _table.SelectedRow?.Id;
        _table.SortColumn = _sortColumn;
        _table.SortDescending = _sortDescending;
        _rows.ReplaceAll(shown);

        long cursor = selectedId is { } id ? shown.FindIndex(p => p.Id == id) : -1;
        if (cursor < 0)
        {
            cursor = Math.Clamp(_table.ScrollableModel.CursorPosition.Y, 0, Math.Max(0, shown.Count - 1));
        }

        _table.ScrollableModel.CursorPosition.Y = cursor;
        _table.ScrollableModel.ScrollToVisible();
        _summary.Text = string.Format(UiText.Get("Process.Shown", "{0} of {1} processes"), shown.Count, latest.Count);
    }

    private static bool Matches(ProcessSample process, string filter) =>
        process.Name.Contains(filter, StringComparison.InvariantCultureIgnoreCase)
        || process.Id.ToString(CultureInfo.InvariantCulture).Contains(filter, StringComparison.Ordinal);

    private IEnumerable<ProcessSample> Sorted(IEnumerable<ProcessSample> processes) => _sortColumn switch
    {
        NameColumn => Order(processes, p => p.Name, StringComparer.CurrentCultureIgnoreCase),
        IdColumn => Order(processes, p => p.Id),
        CpuColumn => Order(processes, p => p.CpuPercent ?? -1),
        _ => Order(processes, p => p.WorkingSetBytes ?? -1),
    };

    private IEnumerable<ProcessSample> Order<TKey>(IEnumerable<ProcessSample> processes, Func<ProcessSample, TKey> key, IComparer<TKey>? comparer = null) =>
        _sortDescending
            ? processes.OrderByDescending(key, comparer).ThenBy(p => p.Id)
            : processes.OrderBy(key, comparer).ThenBy(p => p.Id);

    /// <summary>
    /// Sorts by <paramref name="column"/>. Choosing the column that is already sorted reverses the order; a new column starts with
    /// the order that suits it: the biggest CPU and memory users first, names and ids in ascending order.
    /// </summary>
    private void SortBy(int column)
    {
        if (column == _sortColumn)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = column is CpuColumn or MemoryColumn;
        }

        _viewChanged = true;
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Type", UiText.Get("DialogHint.FilterProcesses", "Filter the processes"));
        yield return ("Up/Down", UiText.Get("DialogHint.MoveOverProcesses", "Move over the processes, even from the filter box"));
        yield return ("PageUp/PageDown", UiText.Get("Hint.MovePageAtATime", "Move a page at a time"));
        yield return ("Ctrl+Home/End", UiText.Get("DialogHint.JumpFirstLastProcess", "Jump to the first or last process"));
        yield return ("Ctrl+Left/Right", UiText.Get("DialogHint.ProcessSortColumn", "Choose the column to sort by (table focused)"));
        yield return ("Ctrl+Down", UiText.Get("DialogHint.ProcessSortDirection", "Reverse the sort order"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_textbox.IsFocused
            && keyEvent.ConsoleKey is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.PageUp or ConsoleKey.PageDown)
        {
            return await _table.HandleKeyEventAsync(keyEvent);
        }

        // Home and End belong to the filter's cursor, so Ctrl+Home and Ctrl+End reach the first and the last process.
        if (_textbox.IsFocused && keyEvent.Control && !keyEvent.Alt
            && keyEvent.ConsoleKey is ConsoleKey.Home or ConsoleKey.End)
        {
            return await _table.HandleKeyEventAsync(new KeyEvent(keyEvent.KeyChar, keyEvent.ConsoleKey, false, false, false));
        }

        // Ctrl+Left and Ctrl+Right stay with the filter's word movement while it has focus.
        if (_table.IsFocused && keyEvent.Control && !keyEvent.Alt
            && keyEvent.ConsoleKey is ConsoleKey.LeftArrow or ConsoleKey.RightArrow)
        {
            int step = keyEvent.ConsoleKey == ConsoleKey.RightArrow ? 1 : ColumnCount - 1;
            SortBy((_sortColumn + step) % ColumnCount);
            return true;
        }

        if (keyEvent.Control && !keyEvent.Alt && keyEvent.ConsoleKey == ConsoleKey.DownArrow)
        {
            SortBy(_sortColumn);
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }
}

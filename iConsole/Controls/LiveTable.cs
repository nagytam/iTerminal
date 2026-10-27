using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using System.Globalization;

namespace iConsole.Controls;

/// <summary>A column of a <see cref="LiveTable"/>: a header, a width (0 fits the content) and how text sits in it.</summary>
public sealed record LiveTableColumn(string Header, int Width = 0, TextAlignment Alignment = TextAlignment.Left);

/// <summary>
/// A table whose rows are updated while it is shown, in the manner of Spectre.Console's live display: rows are named by a key,
/// <see cref="SetRow"/> and <see cref="SetCell"/> add or change them from any thread, and a cell whose text changed is
/// highlighted for <see cref="FlashDuration"/> so the eye finds it. The table repaints from <see cref="OnIdle"/> (a change, or
/// a highlight about to end), so workers never touch the control tree. Rows keep the order they were added in unless
/// <see cref="SortColumn"/> is set (numbers sort as numbers). Up, Down, PageUp, PageDown, Home and End scroll when it is focused;
/// <see cref="KeepNewestVisible"/> follows the last row instead.
/// </summary>
public class LiveTable : Control
{
    private const int FlashStepMilliseconds = 100;

    private sealed class Row(string key)
    {
        public string Key { get; } = key;

        public List<string> Cells { get; } = [];

        public List<long> ChangedAt { get; } = [];

        public Dictionary<int, Color> Colors { get; } = [];
    }

    private readonly ITimeProvider _time;
    private readonly Lock _gate = new();
    private readonly List<LiveTableColumn> _columns = [];
    private readonly List<Row> _rows = [];
    private long _version;
    private long _drawnVersion = -1;
    private long _drawnBucket = -1;
    private int _top;
    private int _sortColumn = -1;
    private bool _sortDescending;
    private bool _keepNewestVisible;
    private TimeSpan _flashDuration = TimeSpan.FromMilliseconds(600);

    public LiveTable(IThemeService themeService, ITimeProvider time) : base(themeService)
    {
        _time = time;
        CanFocus = true;
        ViewSize = new Size(60, 8);
    }

    private void Changed() => Interlocked.Increment(ref _version);

    /// <summary>How long a changed cell stays highlighted; zero turns the highlight off.</summary>
    public TimeSpan FlashDuration
    {
        get => _flashDuration;
        set { _flashDuration = value < TimeSpan.Zero ? TimeSpan.Zero : value; Changed(); }
    }

    /// <summary>The column the rows are sorted by, or -1 for the order they were added in.</summary>
    public int SortColumn
    {
        get { lock (_gate) { return _sortColumn; } }
        set { lock (_gate) { _sortColumn = value; } Changed(); }
    }

    public bool SortDescending
    {
        get { lock (_gate) { return _sortDescending; } }
        set { lock (_gate) { _sortDescending = value; } Changed(); }
    }

    /// <summary>Scrolls to the last row whenever one is added.</summary>
    public bool KeepNewestVisible
    {
        get => _keepNewestVisible;
        set { _keepNewestVisible = value; Changed(); }
    }

    public string EmptyText
    {
        get => _emptyText;
        set => _emptyText = value ?? string.Empty;
    }

    private string _emptyText = "No rows";

    public IReadOnlyList<LiveTableColumn> Columns
    {
        get { lock (_gate) { return [.. _columns]; } }
    }

    public int RowCount
    {
        get { lock (_gate) { return _rows.Count; } }
    }

    /// <summary>The keys of the rows in display order.</summary>
    public IReadOnlyList<string> Keys
    {
        get { lock (_gate) { return [.. Ordered().Select(r => r.Key)]; } }
    }

    /// <summary>Adds a column and returns its index. Rows already present get an empty cell.</summary>
    public int AddColumn(string header, int width = 0, TextAlignment alignment = TextAlignment.Left)
    {
        lock (_gate)
        {
            _columns.Add(new LiveTableColumn(header ?? string.Empty, Math.Max(0, width), alignment));
            foreach (Row row in _rows)
            {
                Pad(row);
            }
        }

        Changed();
        return _columns.Count - 1;
    }

    private void Pad(Row row)
    {
        while (row.Cells.Count < _columns.Count)
        {
            row.Cells.Add(string.Empty);
            row.ChangedAt.Add(long.MinValue);
        }
    }

    private Row Find(string key)
    {
        Row? row = _rows.Find(r => r.Key == key);
        if (row is null)
        {
            row = new Row(key);
            _rows.Add(row);
            Pad(row);
        }

        return row;
    }

    /// <summary>Adds the row <paramref name="key"/> or replaces its cells; cells beyond the columns are ignored and missing ones are left alone.</summary>
    public void SetRow(string key, params string[] cells)
    {
        lock (_gate)
        {
            Row row = Find(key);
            for (int i = 0; i < Math.Min(cells.Length, _columns.Count); i++)
            {
                Update(row, i, cells[i] ?? string.Empty);
            }
        }

        Changed();
    }

    /// <summary>Sets one cell, adding the row if it is new. <paramref name="color"/> tints the cell's text; null clears the tint.</summary>
    public void SetCell(string key, int column, string text, Color? color = null)
    {
        lock (_gate)
        {
            if (column < 0 || column >= _columns.Count)
            {
                return;
            }

            Row row = Find(key);
            Update(row, column, text ?? string.Empty);
            if (color is { } tint)
            {
                row.Colors[column] = tint;
            }
            else
            {
                row.Colors.Remove(column);
            }
        }

        Changed();
    }

    private void Update(Row row, int column, string text)
    {
        if (row.Cells[column] != text)
        {
            // A cell filled for the first time is not "changed"; only an edit of something already shown flashes.
            bool wasEmpty = row.Cells[column].Length == 0;
            row.Cells[column] = text;
            row.ChangedAt[column] = wasEmpty ? long.MinValue : _time.MonotonicMilliseconds;
        }
    }

    /// <summary>The text of a cell, or null when the row or column does not exist.</summary>
    public string? GetCell(string key, int column)
    {
        lock (_gate)
        {
            Row? row = _rows.Find(r => r.Key == key);
            return row is null || column < 0 || column >= row.Cells.Count ? null : row.Cells[column];
        }
    }

    public bool RemoveRow(string key)
    {
        bool removed;
        lock (_gate)
        {
            removed = _rows.RemoveAll(r => r.Key == key) > 0;
        }

        Changed();
        return removed;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _rows.Clear();
            _top = 0;
        }

        Changed();
    }

    private IEnumerable<Row> Ordered()
    {
        if (_sortColumn < 0 || _sortColumn >= _columns.Count)
        {
            return _rows;
        }

        int column = _sortColumn;
        IOrderedEnumerable<Row> sorted = _sortDescending
            ? _rows.OrderByDescending(r => r, Comparer<Row>.Create((a, b) => CompareCells(a.Cells[column], b.Cells[column])))
            : _rows.OrderBy(r => r, Comparer<Row>.Create((a, b) => CompareCells(a.Cells[column], b.Cells[column])));
        return sorted;
    }

    /// <summary>Numbers compare as numbers (so 9 sorts before 10), anything else as text ignoring case.</summary>
    public static int CompareCells(string a, string b)
    {
        bool numberA = double.TryParse(a.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double x);
        bool numberB = double.TryParse(b.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double y);
        if (numberA && numberB)
        {
            return x.CompareTo(y);
        }

        return numberA != numberB ? (numberA ? -1 : 1) : string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
    }

    private int Body => Math.Max(1, (int)ViewSize.Height - 1);

    private void ScrollBy(int rows)
    {
        lock (_gate)
        {
            _top = Math.Clamp(_top + rows, 0, Math.Max(0, _rows.Count - Body));
        }

        Changed();
    }

    public override void OnIdle()
    {
        base.OnIdle();
        if (Interlocked.Read(ref _version) != _drawnVersion || FlashBucket() != _drawnBucket)
        {
            StateHasChanged();
        }
    }

    /// <summary>Changes while a highlight is on screen, so it fades on time even when nothing else changes.</summary>
    private long FlashBucket()
    {
        long now = _time.MonotonicMilliseconds;
        lock (_gate)
        {
            bool flashing = _flashDuration > TimeSpan.Zero
                && _rows.Any(r => r.ChangedAt.Any(t => t != long.MinValue && now - t < _flashDuration.TotalMilliseconds + FlashStepMilliseconds));
            return flashing ? now / FlashStepMilliseconds : 0;
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled)
        {
            yield break;
        }

        yield return ("Up/Down", "Scroll one row");
        yield return ("PageUp/PageDown", "Scroll one page");
        yield return ("Home/End", "First or last row");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled || !IsFocused || !IsEnabled || keyEvent.Alt || keyEvent.Control)
        {
            return isHandled;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.UpArrow: ScrollBy(-1); return true;
            case ConsoleKey.DownArrow: ScrollBy(1); return true;
            case ConsoleKey.PageUp: ScrollBy(-Body); return true;
            case ConsoleKey.PageDown: ScrollBy(Body); return true;
            case ConsoleKey.Home: ScrollBy(int.MinValue / 2); return true;
            case ConsoleKey.End: ScrollBy(int.MaxValue / 2); return true;
        }

        return false;
    }

    /// <summary>The widths the columns get in <paramref name="available"/> cells (gaps included), shrinking the widest fitted columns first.</summary>
    public static int[] ComputeWidths(IReadOnlyList<LiveTableColumn> columns, IReadOnlyList<int> contentWidths, int available)
    {
        int[] widths = [.. columns.Select((c, i) => c.Width > 0 ? c.Width : Math.Max(c.Header.Length, contentWidths[i]))];
        int gaps = Math.Max(0, columns.Count - 1);
        while (widths.Sum() + gaps > available)
        {
            // Shrink the widest column that is allowed to (fitted ones first), never below 3.
            int pick = -1;
            for (int i = 0; i < widths.Length; i++)
            {
                if (widths[i] > 3 && (pick < 0 || (columns[i].Width == 0 && columns[pick].Width > 0) || (columns[i].Width == 0 == (columns[pick].Width == 0) && widths[i] > widths[pick])))
                {
                    pick = i;
                }
            }

            if (pick < 0)
            {
                break;
            }

            widths[pick]--;
        }

        return widths;
    }

    private static string Fit(string text, int width, TextAlignment alignment)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        if (text.Length > width)
        {
            return width == 1 ? "…" : text[..(width - 1)] + "…";
        }

        return alignment switch
        {
            TextAlignment.Right => text.PadLeft(width),
            TextAlignment.Center => text.PadLeft((width + text.Length) / 2).PadRight(width),
            _ => text.PadRight(width),
        };
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        int width = (int)ViewSize.Width;
        int height = (int)ViewSize.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        long now = _time.MonotonicMilliseconds;
        _drawnVersion = Interlocked.Read(ref _version);
        _drawnBucket = FlashBucket();
        ColorScheme scheme = ThemeService.CurrentColorScheme;
        var grid = new CellGrid(width, height);
        IReadOnlyList<LiveTableColumn> columns;
        List<(string[] Cells, long[] Changed, Dictionary<int, Color> Colors)> rows;
        int top;
        lock (_gate)
        {
            columns = [.. _columns];
            rows = [.. Ordered().Select(r => (r.Cells.ToArray(), r.ChangedAt.ToArray(), new Dictionary<int, Color>(r.Colors)))];
            if (_keepNewestVisible)
            {
                _top = Math.Max(0, rows.Count - Body);
            }

            _top = Math.Clamp(_top, 0, Math.Max(0, rows.Count - Body));
            top = _top;
        }

        if (columns.Count == 0)
        {
            grid.Text(0, 0, EmptyText, scheme.DisabledForeground);
            grid.WriteTo(buffer, ZOrder, AbsolutePosition, scheme.Foreground, scheme.Background);
            return;
        }

        int[] contentWidths = [.. columns.Select((_, i) => rows.Select(r => r.Cells[i].Length).DefaultIfEmpty(0).Max())];
        int[] widths = ComputeWidths(columns, contentWidths, width);
        DrawLine(grid, 0, columns, widths, columns.Select(c => c.Header).ToArray(), null, null, now, scheme, header: true);
        for (int line = 0; line < height - 1; line++)
        {
            int index = top + line;
            if (index < rows.Count)
            {
                DrawLine(grid, line + 1, columns, widths, rows[index].Cells, rows[index].Changed, rows[index].Colors, now, scheme, header: false);
            }
            else if (line == 0 && rows.Count == 0)
            {
                grid.Text(0, 1, EmptyText.Length > width ? EmptyText[..width] : EmptyText, scheme.DisabledForeground);
            }
        }

        grid.WriteTo(buffer, ZOrder, AbsolutePosition, scheme.Foreground, scheme.Background);
    }

    private void DrawLine(
        CellGrid grid, int line, IReadOnlyList<LiveTableColumn> columns, int[] widths, string[] cells, long[]? changedAt,
        Dictionary<int, Color>? colors, long now, ColorScheme scheme, bool header)
    {
        int x = 0;
        if (header)
        {
            grid.Fill(0, line, (int)ViewSize.Width, scheme.SelectedForeground, scheme.SelectedBackground);
        }

        for (int i = 0; i < columns.Count; i++)
        {
            string text = Fit(cells[i], widths[i], columns[i].Alignment);
            Color? foreground = header ? scheme.SelectedForeground : colors is not null && colors.TryGetValue(i, out Color tint) ? tint : null;
            Color? background = header ? scheme.SelectedBackground : null;
            if (!header && changedAt is not null && changedAt[i] != long.MinValue
                && _flashDuration > TimeSpan.Zero && now - changedAt[i] < _flashDuration.TotalMilliseconds)
            {
                foreground = scheme.SelectedForeground;
                background = scheme.SelectedBackground;
            }

            grid.Text(x, line, text, foreground, background);
            x += widths[i] + 1;
        }
    }
}

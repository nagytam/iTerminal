using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using System.Collections.Specialized;

namespace iConsole.Controls;

/// <summary>Defines one column in a <see cref="Table{T}"/>.</summary>
public sealed class ColumnDefinition<T>(string header, int width, Func<T, string> valueSelector)
{
    public string Header { get; } = header;
    public int Width { get; } = width;
    public Func<T, string> ValueSelector { get; } = valueSelector;
}

/// <summary>
/// Columnar data control with a fixed header row and a keyboard-navigable, scrollable body.
/// Wire up columns via <see cref="Columns"/> before the control is laid out.
/// </summary>
public class Table<T> : Control
{
    private readonly ConcurrentObservableCollection<T> _rows;

    public Table(ConcurrentObservableCollection<T> rows, IThemeService themeService)
        : base(themeService)
    {
        _rows = rows;
        ScrollableModel = new ScrollableModel();
        CanFocus = true;

        AddCommand(new ScrollDownCommand(ScrollableModel));
        AddCommand(new ScrollUpCommand(ScrollableModel));
        AddCommand(new ScrollPageDownCommand(ScrollableModel));
        AddCommand(new ScrollPageUpCommand(ScrollableModel));
        AddCommand(new ScrollToHomeVerticalCommand(ScrollableModel));
        AddCommand(new ScrollToEndVerticalCommand(ScrollableModel));

        _rows.CollectionChanged += OnRowsChanged;
    }

    public List<ColumnDefinition<T>> Columns { get; } = [];

    public ScrollableModel ScrollableModel { get; }

    /// <summary>Zero-based index of the highlighted row in <see cref="_rows"/>.</summary>
    public int CursorRow => (int)ScrollableModel.CursorPosition.Y;

    /// <summary>The data item at the cursor, or <c>default</c> when the collection is empty.</summary>
    public T? SelectedRow => _rows.Count > CursorRow ? _rows[CursorRow] : default;

    /// <summary>
    /// Drawn on the first body row, in the disabled colours, while the table has no rows. Empty by
    /// default.
    /// </summary>
    public string EmptyText { get; set; } = string.Empty;

    /// <summary>Raised when the user presses Enter on a row.</summary>
    public event EventHandler<T?>? RowActivated;

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollableModel.ScrollableSize = new Size(ViewSize.Width, _rows.Count);
        ScrollableModel.Update();
        StateHasChanged();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        var bodyHeight = Math.Max(0, (int)ViewSize.Height - 1); // row 0 is the header
        ScrollableModel.ViewSize = new Size(ViewSize.Width, bodyHeight);
        ScrollableModel.ScrollableSize = new Size(ViewSize.Width, _rows.Count);
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // Enter on an empty table has no row to activate; leave the key to whoever hosts the table.
        if (keyEvent.ConsoleKey == ConsoleKey.Enter && _rows.Count > 0)
        {
            RowActivated?.Invoke(this, SelectedRow);
            return true;
        }
        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;

        // Header row
        var header = BuildRow(Columns.Select(c => c.Header), Columns.Select(c => c.Width));
        buffer.Write(ZOrder, AbsolutePosition, ViewSize,
            new ConxelString(header, colorScheme.SelectedForeground, colorScheme.SelectedBackground));

        // Data rows (starting at y = 1)
        var bodyHeight = Math.Max(0, (int)ViewSize.Height - 1);
        var scrollOffset = (int)ScrollableModel.ScrollPosition.Y;
        var snapshot = _rows.AsSnapshot();

        for (int y = 0; y < bodyHeight; y++)
        {
            var rowIndex = scrollOffset + y;
            var rowPosition = new Position(AbsolutePosition.X, (uint)(AbsolutePosition.Y + 1 + y));
            if (rowIndex >= snapshot.Count)
            {
                // Paint the rows below the data too: a table that loses rows must not keep
                // showing them, and nothing else clears the layer between frames.
                var filler = snapshot.Count == 0 && y == 0 ? EmptyText : string.Empty;
                var fillerForeground = filler.Length > 0 ? colorScheme.DisabledForeground : colorScheme.Foreground;
                buffer.Write(ZOrder, rowPosition, ViewSize,
                    new ConxelString(filler.PadRight((int)ViewSize.Width), fillerForeground, colorScheme.Background));
                continue;
            }

            var row = snapshot[rowIndex];
            var text = BuildRow(Columns.Select(c => c.ValueSelector(row)), Columns.Select(c => c.Width));

            var visualState = rowIndex == CursorRow
                ? (IsFocused ? VisualState.Focused : VisualState.Selected)
                : VisualState.Normal;
            GetColor(colorScheme, visualState, out var fg, out var bg);

            buffer.Write(ZOrder, rowPosition, ViewSize, new ConxelString(text, fg, bg));
        }
    }

    public override void GetColor(ColorScheme colorScheme, VisualState visualState, out Color foregroundColor, out Color backgroundColor)
    {
        switch (visualState)
        {
            case VisualState.Focused:
                foregroundColor = colorScheme.SelectedFocusedForeground;
                backgroundColor = colorScheme.SelectedFocusedBackground;
                break;
            case VisualState.Selected:
                foregroundColor = colorScheme.SelectedForeground;
                backgroundColor = colorScheme.SelectedBackground;
                break;
            case VisualState.Disabled:
                foregroundColor = colorScheme.DisabledForeground;
                backgroundColor = colorScheme.DisabledBackground;
                break;
            default:
                foregroundColor = colorScheme.Foreground;
                backgroundColor = colorScheme.Background;
                break;
        }
    }

    private string BuildRow(IEnumerable<string> values, IEnumerable<int> widths)
    {
        var parts = values.Zip(widths, (v, w) => v.Cut(w).PadRight(w));
        var row = string.Join(" ", parts);
        return row.PadRight((int)ViewSize.Width);
    }
}

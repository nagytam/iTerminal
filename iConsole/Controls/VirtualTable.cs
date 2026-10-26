using iConsole.Buffers;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A table that reads only the rows on screen from an <see cref="IVirtualDataSource{T}"/>: a header row, then the body.
/// Columns are <see cref="ColumnDefinition{T}"/>s as for <see cref="Table{T}"/>, and the header works the same way: it can
/// show a sort arrow and raises <see cref="SortRequested"/> on a click. The rows are not sorted here; a source that
/// sorts (a paged query, say) is told by the host and <see cref="PagedDataSource{T}.Reset"/>.
/// </summary>
public class VirtualTable<T> : VirtualRowsControl<T>
{
    public VirtualTable(IVirtualDataSource<T> source, IThemeService themeService) : base(source, themeService)
    {
    }

    public List<ColumnDefinition<T>> Columns { get; } = [];

    /// <summary>The column the host has sorted the rows by, or -1; its header shows ▲ or ▼.</summary>
    public int SortColumn { get; set; } = -1;

    /// <summary>Whether <see cref="SortColumn"/> is sorted descending (▼).</summary>
    public bool SortDescending { get; set; }

    /// <summary>Raised with the column index when the user clicks a header cell.</summary>
    public event EventHandler<int>? SortRequested;

    protected override int HeaderRows => 1;

    /// <summary>The column whose header cell covers <paramref name="x"/> (relative to the table), or -1.</summary>
    public int ColumnAt(int x)
    {
        int start = 0;
        for (int i = 0; i < Columns.Count; i++)
        {
            if (x >= start && x < start + Columns[i].Width)
            {
                return i;
            }

            start += Columns[i].Width + 1;
        }

        return -1;
    }

    /// <summary>The header text of column <paramref name="index"/>, with the sort arrow at the cell's right edge.</summary>
    public string HeaderText(int index)
    {
        string header = Columns[index].Header;
        if (index != SortColumn || Columns[index].Width < 2)
        {
            return header;
        }

        int room = Columns[index].Width - 1;
        return header.Cut(room).PadRight(room) + (SortDescending ? "▼" : "▲");
    }

    protected override string RowText(T item, int index)
    {
        return string.Join(" ", Columns.Select(column => column.ValueSelector(item).Cut(column.Width).PadRight(column.Width)));
    }

    protected override bool OnHeaderClick(int x)
    {
        int column = ColumnAt(x);
        if (column < 0)
        {
            return false;
        }

        if (!IsFocused)
        {
            SetFocus();
        }

        SortRequested?.Invoke(this, column);
        return true;
    }

    protected override void DrawHeader(IBuffer buffer, ColorScheme colorScheme)
    {
        int width = (int)ViewSize.Width;
        string header = string.Join(" ", Columns.Select((column, i) => HeaderText(i).Cut(column.Width).PadRight(column.Width)));
        buffer.Write(ZOrder, AbsolutePosition, ViewSize,
            new ConxelString(header.Cut(width).PadRight(width), colorScheme.SelectedForeground, colorScheme.SelectedBackground));
    }
}

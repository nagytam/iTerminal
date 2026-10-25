using iConsole.Buffers;
using iConsole.Data;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>How a grid row or column is sized.</summary>
public enum GridUnit
{
    /// <summary>Exactly this many cells.</summary>
    Cells,

    /// <summary>As wide as its widest child (or tallest, for a row) has its size now.</summary>
    Auto,

    /// <summary>A share of the space left over after the other tracks, in proportion to its weight.</summary>
    Star,
}

/// <summary>The size of one grid track. Use <see cref="Cells"/>, <see cref="Auto"/> or <see cref="Star"/>.</summary>
public readonly record struct GridLength(GridUnit Unit, int Value)
{
    public static GridLength Auto => new(GridUnit.Auto, 0);

    public static GridLength Cells(int cells) => new(GridUnit.Cells, Math.Max(0, cells));

    public static GridLength Star(int weight = 1) => new(GridUnit.Star, Math.Max(1, weight));
}

/// <summary>
/// A grid of rows and columns that places its children and sizes them to fill the grid's rectangle.
///
/// <para>Each child goes into one cell or spans several, with <see cref="Place"/>. Fixed tracks take their cells,
/// auto tracks take the largest child's size, and star tracks share what is left by weight. Children are
/// positioned relative to the grid, so a grid is itself placed like any control.</para>
///
/// <para>The grid arranges itself when it is laid out and again whenever its size changes. Changing <see cref="Columns"/>
/// or <see cref="Rows"/> after that needs a call to <see cref="Arrange"/>; so does <see cref="Place"/>, which arranges
/// once the child is added.</para>
/// </summary>
public class Grid : Control
{
    private readonly List<Placement> _placements = [];
    // Null until the first arrangement, so the first draw always arranges.
    private Size? _arrangedSize;

    public Grid(IThemeService themeService) : base(themeService)
    {
    }

    /// <summary>The column tracks, left to right. A grid with none has one star column.</summary>
    public List<GridLength> Columns { get; } = [GridLength.Star()];

    /// <summary>The row tracks, top to bottom. A grid with none has one star row.</summary>
    public List<GridLength> Rows { get; } = [GridLength.Star()];

    /// <summary>Where a child sits: its first row and column, and how many rows and columns it covers.</summary>
    public sealed record Placement(Control Child, int Row, int Column, int RowSpan, int ColumnSpan);

    /// <summary>The children and where they are placed, in the order they were added.</summary>
    public IReadOnlyList<Placement> Placements => _placements;

    /// <summary>Adds <paramref name="child"/> to the grid at <paramref name="row"/>, <paramref name="column"/>, covering the given number of tracks.</summary>
    public void Place(Control child, int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (row < 0 || column < 0 || rowSpan < 1 || columnSpan < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(row), "A grid cell starts at a row and column of 0 or more and spans at least one of each.");
        }

        _placements.Add(new Placement(child, row, column, rowSpan, columnSpan));
        AddControl(child);
        Arrange();
    }

    /// <summary>Sizes the tracks to the grid's current size and positions every child in its cells.</summary>
    public void Arrange()
    {
        // A grid with no tracks has one star track in each direction, as its documentation says.
        var columns = Columns.Count == 0 ? [GridLength.Star()] : Columns;
        var rows = Rows.Count == 0 ? [GridLength.Star()] : Rows;
        var columnWidths = SolveTracks(columns, ViewSize.Width,
            index => AutoSize(index, isColumn: true));
        var rowHeights = SolveTracks(rows, ViewSize.Height,
            index => AutoSize(index, isColumn: false));

        foreach (var placement in _placements)
        {
            var left = Sum(columnWidths, 0, placement.Column);
            var top = Sum(rowHeights, 0, placement.Row);
            var width = Sum(columnWidths, placement.Column, placement.ColumnSpan);
            var height = Sum(rowHeights, placement.Row, placement.RowSpan);

            placement.Child.Position = new Position(left, top);
            placement.Child.ViewSize = new Size(width, height);
        }

        _arrangedSize = ViewSize;
        StateHasChanged();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        Arrange();
    }

    public override void Draw(IBuffer buffer)
    {
        if (_arrangedSize is null || ViewSize != _arrangedSize)
        {
            Arrange();
        }

        base.Draw(buffer);
    }

    /// <summary>
    /// The largest size among the children that sit in track <paramref name="index"/> and span only that track. A child that
    /// spans several tracks does not count, since its size is shared out between them.
    /// </summary>
    private long AutoSize(int index, bool isColumn)
    {
        long largest = 0;
        foreach (var placement in _placements)
        {
            var start = isColumn ? placement.Column : placement.Row;
            var span = isColumn ? placement.ColumnSpan : placement.RowSpan;
            if (start != index || span != 1)
            {
                continue;
            }

            largest = Math.Max(largest, isColumn ? placement.Child.ViewSize.Width : placement.Child.ViewSize.Height);
        }
        return largest;
    }

    /// <summary>
    /// Gives each track its size: fixed tracks their cells, auto tracks what <paramref name="autoSize"/> asks, and star tracks
    /// the rest in proportion to their weights. The last star track takes the rounding remainder, so the tracks always add up
    /// to the available space when there is a star track to take it.
    /// </summary>
    private static long[] SolveTracks(IReadOnlyList<GridLength> tracks, long available, Func<int, long> autoSize)
    {
        var sizes = new long[tracks.Count];
        long used = 0;
        long totalWeight = 0;
        var lastStar = -1;

        for (var i = 0; i < tracks.Count; i++)
        {
            switch (tracks[i].Unit)
            {
                case GridUnit.Cells:
                    sizes[i] = tracks[i].Value;
                    break;
                case GridUnit.Auto:
                    sizes[i] = autoSize(i);
                    break;
                case GridUnit.Star:
                    totalWeight += tracks[i].Value;
                    lastStar = i;
                    continue;
            }
            used += sizes[i];
        }

        var remaining = Math.Max(0, available - used);
        long shared = 0;
        for (var i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].Unit != GridUnit.Star)
            {
                continue;
            }

            sizes[i] = i == lastStar
                ? remaining - shared
                : remaining * tracks[i].Value / totalWeight;
            shared += sizes[i];
        }

        return sizes;
    }

    private static long Sum(long[] sizes, int start, int count)
    {
        long total = 0;
        for (var i = start; i < start + count && i < sizes.Length; i++)
        {
            total += sizes[i];
        }
        return total;
    }
}

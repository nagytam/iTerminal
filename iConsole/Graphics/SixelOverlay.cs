using iConsole.Display;

namespace iConsole.Graphics;

/// <summary>A Sixel picture to show over a rectangle of cells, and who asked for it.</summary>
public sealed class SixelPlacement
{
    public SixelPlacement(object owner, int x, int y, int columns, int rows, string data, Color background)
    {
        Owner = owner;
        X = x;
        Y = y;
        Columns = columns;
        Rows = rows;
        Data = data;
        Background = background;
    }

    public object Owner { get; }

    /// <summary>Left column of the rectangle, in screen cells.</summary>
    public int X { get; }

    /// <summary>Top row of the rectangle, in screen cells.</summary>
    public int Y { get; }

    public int Columns { get; }

    public int Rows { get; }

    /// <summary>The complete Sixel sequence.</summary>
    public string Data { get; }

    /// <summary>The background of the blank cells the control paints under the picture.</summary>
    public Color Background { get; }

    public bool IsSameAs(SixelPlacement? other) =>
        other is not null
        && ReferenceEquals(Data, other.Data)
        && ReferenceEquals(Owner, other.Owner)
        && X == other.X && Y == other.Y && Columns == other.Columns && Rows == other.Rows
        && Background == other.Background;
}

/// <summary>
/// The hand-over between a control and the renderer for a picture the character grid cannot hold. The control paints blank cells
/// where the picture goes and offers the picture here on every draw; the renderer draws it on top once the cells are on screen,
/// and takes it away again when anything covers those cells or the control withdraws it. One picture at a time: the newest wins.
/// </summary>
public sealed class SixelOverlay
{
    private volatile SixelPlacement? _current;

    public SixelPlacement? Current => _current;

    /// <summary>Offers a picture; the same picture offered again is ignored, so calling this on every draw is cheap.</summary>
    public void Show(object owner, int x, int y, int columns, int rows, string data, Color background)
    {
        SixelPlacement? current = _current;
        if (current is not null
            && ReferenceEquals(current.Owner, owner)
            && ReferenceEquals(current.Data, data)
            && current.X == x && current.Y == y && current.Columns == columns && current.Rows == rows
            && current.Background == background)
        {
            return;
        }

        _current = new SixelPlacement(owner, x, y, columns, rows, data, background);
    }

    /// <summary>Withdraws the picture, if it is this owner's.</summary>
    public void Hide(object owner)
    {
        SixelPlacement? current = _current;
        if (current is not null && ReferenceEquals(current.Owner, owner))
        {
            Interlocked.CompareExchange(ref _current, null, current);
        }
    }
}

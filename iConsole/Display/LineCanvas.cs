namespace iConsole.Display;

/// <summary>Which way a line runs on a <see cref="LineCanvas"/>.</summary>
public enum LineOrientation
{
    Horizontal,
    Vertical,
}

/// <summary>
/// Lines on a grid of cells that join where they meet: two boxes that share an edge get a <c>┬</c> or <c>┼</c> where the
/// edges cross instead of two corners on top of each other. Each cell remembers which of its four sides a line leaves
/// by (and in which <see cref="BorderStyle"/>), and the glyph is chosen from that, so lines may be added in any order and
/// overlap freely. Where lines of different styles meet the heavier one wins (thick over double over thin); a corner is
/// round only when every line in it is round.
/// </summary>
public sealed class LineCanvas
{
    private const int North = 1;
    private const int East = 2;
    private const int South = 4;
    private const int West = 8;

    private readonly int[,] _masks;
    private readonly BorderStyle[,,] _styles;

    public LineCanvas(int width, int height)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        _masks = new int[Height, Width];
        _styles = new BorderStyle[Height, Width, 4];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Adds a line of <paramref name="length"/> cells from (<paramref name="x"/>, <paramref name="y"/>). A part outside the canvas is ignored.</summary>
    public void AddLine(int x, int y, int length, LineOrientation orientation, BorderStyle style = BorderStyle.Single)
    {
        if (length <= 0 || style == BorderStyle.None)
        {
            return;
        }

        bool horizontal = orientation == LineOrientation.Horizontal;
        for (int i = 0; i < length; i++)
        {
            int cx = horizontal ? x + i : x;
            int cy = horizontal ? y : y + i;
            // A line of one cell has no direction to leave by, so it is drawn straight through.
            int mask = 0;
            if (horizontal)
            {
                mask |= (i > 0 || length == 1) ? West : 0;
                mask |= (i < length - 1 || length == 1) ? East : 0;
            }
            else
            {
                mask |= (i > 0 || length == 1) ? North : 0;
                mask |= (i < length - 1 || length == 1) ? South : 0;
            }

            Mark(cx, cy, mask, style);
        }
    }

    /// <summary>Adds the four edges of a rectangle; its corners join with anything that arrives there.</summary>
    public void AddRectangle(int x, int y, int width, int height, BorderStyle style = BorderStyle.Single)
    {
        if (width < 2 || height < 2)
        {
            return;
        }

        AddLine(x, y, width, LineOrientation.Horizontal, style);
        AddLine(x, y + height - 1, width, LineOrientation.Horizontal, style);
        AddLine(x, y, height, LineOrientation.Vertical, style);
        AddLine(x + width - 1, y, height, LineOrientation.Vertical, style);
    }

    private void Mark(int x, int y, int mask, BorderStyle style)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return;
        }

        _masks[y, x] |= mask;
        for (int side = 0; side < 4; side++)
        {
            if ((mask & (1 << side)) != 0)
            {
                _styles[y, x, side] = Stronger(_styles[y, x, side], style);
            }
        }
    }

    private static BorderStyle Stronger(BorderStyle current, BorderStyle added)
    {
        return current == BorderStyle.None || Weight(added) > Weight(current) ? added : current;
    }

    private static int Weight(BorderStyle style) => style switch
    {
        BorderStyle.Thick => 3,
        BorderStyle.Double => 2,
        BorderStyle.None => 0,
        _ => 1,
    };

    /// <summary>True when a line passes through the cell.</summary>
    public bool HasLine(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && _masks[y, x] != 0;

    /// <summary>The glyph for the cell: a space where there is no line.</summary>
    public char GlyphAt(int x, int y)
    {
        if (!HasLine(x, y))
        {
            return ' ';
        }

        int mask = _masks[y, x];
        BorderStyle heaviest = BorderStyle.None;
        bool allRound = true;
        for (int side = 0; side < 4; side++)
        {
            if ((mask & (1 << side)) == 0)
            {
                continue;
            }

            BorderStyle style = _styles[y, x, side];
            if (Weight(style) > Weight(heaviest))
            {
                heaviest = style;
            }

            allRound &= style == BorderStyle.Rounded;
        }

        return Glyph(mask, allRound ? BorderStyle.Rounded : heaviest);
    }

    // Indexed by North=1, East=2, South=4, West=8 combined.
    private const string Single = " ╵╶└╷│┌├╴┘─┴┐┤┬┼";
    private const string Thick = " ╹╺┗╻┃┏┣╸┛━┻┓┫┳╋";
    private const string Double = " ║═╚║║╔╠═╝═╩╗╣╦╬";

    /// <summary>The glyph for a set of sides (North 1, East 2, South 4, West 8) in a style.</summary>
    public static char Glyph(int sides, BorderStyle style)
    {
        sides &= 15;
        if (style == BorderStyle.Rounded)
        {
            switch (sides)
            {
                case North | East: return '╰';
                case East | South: return '╭';
                case North | West: return '╯';
                case South | West: return '╮';
            }
        }

        string table = style switch
        {
            BorderStyle.Thick => Thick,
            BorderStyle.Double => Double,
            _ => Single,
        };
        return table[sides];
    }
}

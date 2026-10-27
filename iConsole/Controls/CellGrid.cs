using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;

namespace iConsole.Controls;

/// <summary>
/// A grid of coloured cells painted before one write per row, for controls that draw many differently coloured pieces
/// (a progress row, a table row). A write outside the grid is ignored, and a null colour is the control's foreground.
/// </summary>
internal sealed class CellGrid
{
    private readonly char[,] _chars;
    private readonly Color?[,] _foregrounds;
    private readonly Color?[,] _backgrounds;

    public CellGrid(int width, int height)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        _chars = new char[Height, Width];
        _foregrounds = new Color?[Height, Width];
        _backgrounds = new Color?[Height, Width];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                _chars[y, x] = ' ';
            }
        }
    }

    public int Width { get; }

    public int Height { get; }

    public void Set(int x, int y, char c, Color? foreground = null, Color? background = null)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
        {
            _chars[y, x] = c;
            _foregrounds[y, x] = foreground;
            _backgrounds[y, x] = background;
        }
    }

    public void Text(int x, int y, string text, Color? foreground = null, Color? background = null)
    {
        for (int i = 0; i < text.Length; i++)
        {
            Set(x + i, y, text[i], foreground, background);
        }
    }

    /// <summary>Paints the background of a stretch of a row, keeping what is written there.</summary>
    public void Fill(int x, int y, int length, Color? foreground, Color? background)
    {
        for (int i = 0; i < length; i++)
        {
            if (x + i >= 0 && x + i < Width && y >= 0 && y < Height)
            {
                _foregrounds[y, x + i] ??= foreground;
                _backgrounds[y, x + i] = background;
            }
        }
    }

    public void WriteTo(IBuffer buffer, int zOrder, Position origin, Color foreground, Color background)
    {
        for (int y = 0; y < Height; y++)
        {
            var row = new ConxelString(string.Empty, foreground, background);
            for (int x = 0; x < Width; x++)
            {
                row.Append(_chars[y, x], _foregrounds[y, x] ?? foreground, _backgrounds[y, x] ?? background);
            }

            buffer.Write(zOrder, new Position(origin.X, origin.Y + y), new Size(Width, 1), row);
        }
    }
}

using iConsole.Display;

namespace iTerminal.Core.ScreenSavers;

/// <summary>
/// The cells one frame of a screen saver is drawn into. A new canvas is made when the window changes size, and it is
/// cleared before every frame, so an effect only has to paint what is lit.
/// </summary>
public sealed class ScreenSaverCanvas
{
    private readonly Conxel[] _cells;

    public ScreenSaverCanvas(int width, int height)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        _cells = new Conxel[Width * Height];
        Clear();
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Fills every cell with a blank on black.</summary>
    public void Clear()
    {
        Array.Fill(_cells, new Conxel(' ', Color.Gray, Color.Black));
    }

    /// <summary>Writes one cell. A position off the canvas is ignored, so an effect need not clip.</summary>
    public void Put(int x, int y, char character, Color foreground, bool isBold = false)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return;
        }

        _cells[(y * Width) + x] = new Conxel(character, foreground, Color.Black, isBold);
    }

    /// <summary>One row of the canvas, as a string to draw.</summary>
    public ConxelString Row(int y) => new(_cells.AsSpan(y * Width, Width));
}

using iConsole.Buffers;
using iConsole.Data;

namespace iConsole.Display;

/// <summary>
/// The drop shadow of a popup, dialog or panel: a strip down the right and along the bottom, offset from the window, in a
/// colour that stands out from the theme background (black on a light theme, dark gray on a black one). A cell is about
/// twice as tall as wide, so the default shadow is two cells wide and one tall. It is drawn with the window's own z-order and
/// before the window, so the window covers the part that is underneath it.
/// </summary>
public static class Shadow
{
    /// <summary>Default width of the strip on the right, in cells.</summary>
    public const int DefaultOffsetX = 2;

    /// <summary>Default height of the strip at the bottom, in cells.</summary>
    public const int DefaultOffsetY = 1;

    /// <summary>The colour of the shadow for a theme: dark gray when the background is already black.</summary>
    public static Color ColorFor(ColorScheme scheme) => scheme.Background == Color.Black ? Color.DarkGray : Color.Black;

    /// <summary>
    /// Draws the shadow of a window at <paramref name="origin"/> that is <paramref name="size"/> big. Cells beyond the
    /// buffer are left out.
    /// </summary>
    public static void Draw(IBuffer buffer, int zOrder, Position origin, Size size, ColorScheme scheme, int offsetX = DefaultOffsetX, int offsetY = DefaultOffsetY)
    {
        if (size.Width <= 0 || size.Height <= 0 || offsetX <= 0 && offsetY <= 0)
        {
            return;
        }

        long bufferWidth = buffer.Size.Width;
        long bufferHeight = buffer.Size.Height;
        Color color = ColorFor(scheme);
        long left = origin.X;
        long top = origin.Y;
        long right = left + size.Width;
        long bottom = top + size.Height;

        // The right strip runs from one row below the top to the bottom of the shadow; the bottom strip from the offset to the right end.
        for (long y = top + offsetY; y < bottom + offsetY; y++)
        {
            Strip(buffer, zOrder, right, y, Math.Max(0, offsetX), bufferWidth, bufferHeight, color);
        }

        for (long y = bottom; y < bottom + offsetY; y++)
        {
            long from = left + offsetX;
            Strip(buffer, zOrder, from, y, (int)Math.Max(0, right - from), bufferWidth, bufferHeight, color);
        }
    }

    private static void Strip(IBuffer buffer, int zOrder, long x, long y, int length, long bufferWidth, long bufferHeight, Color color)
    {
        if (y < 0 || y >= bufferHeight || x >= bufferWidth)
        {
            return;
        }

        long start = Math.Max(0, x);
        long end = Math.Min(bufferWidth, x + length);
        if (end <= start)
        {
            return;
        }

        var cells = new ConxelString(new string(' ', (int)(end - start)), color, color);
        buffer.Write(zOrder, new Position(start, y), new Size(end - start, 1), cells);
    }
}

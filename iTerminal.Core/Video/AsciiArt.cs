using iConsole.Display;

namespace iTerminal.Core.Video;

/// <summary>One video frame as characters: ready to be written to the screen without further work.</summary>
public sealed class AsciiFrame(int columns, int rows)
{
    public int Columns { get; } = columns;

    public int Rows { get; } = rows;

    /// <summary>Row-major, <see cref="Rows"/> × <see cref="Columns"/>.</summary>
    public char[] Characters { get; } = new char[columns * rows];

    public Color[] Foregrounds { get; } = new Color[columns * rows];

    /// <summary>The frame's text, one line per row. For tests and debugging.</summary>
    public string AsText()
    {
        var lines = new string[Rows];
        for (var row = 0; row < Rows; row++)
        {
            lines[row] = new string(Characters, row * Columns, Columns);
        }

        return string.Join('\n', lines);
    }
}

public enum AsciiColorMode
{
    /// <summary>Brightness only: white and grey on black.</summary>
    Mono,

    /// <summary>Brightness picks the character, the pixel's hue picks one of the 16 console colours.</summary>
    Color,
}

/// <summary>
/// Turns packed RGB pixels into <see cref="AsciiFrame"/>s. The console only has 16 colours, so
/// the picture is carried mostly by the character: a ramp from blank to dense, indexed by luma.
/// </summary>
public static class AsciiArt
{
    /// <summary>Dark to light. A terminal draws light-on-dark, so a denser glyph reads as a brighter pixel.</summary>
    public const string Ramp = " .'`^\",:;Il!i><~+_-?][}{1)(|\\/tfjrxnuvczXYUJCLQ0OZmwqpdbkhao*#MW&8%B@$";

    /// <summary>Terminal cells are about twice as tall as they are wide.</summary>
    public const double CellAspect = 0.5;

    /// <summary>Below this brightest channel a pixel is drawn as a blank cell, so black stays black in colour mode.</summary>
    private const int BlackLevel = 24;

    /// <summary>Above this brightest channel the intense variant of the colour is used.</summary>
    private const int BrightLevel = 150;

    /// <summary>A channel counts as present in the hue when it is at least this fraction of the brightest.</summary>
    private const double HueThreshold = 0.55;

    /// <summary>
    /// The largest <c>columns × rows</c> that fits the area without distorting a picture of the given
    /// aspect ratio, allowing for cells being taller than wide.
    /// </summary>
    public static (int Columns, int Rows) Fit(double pictureAspect, int areaColumns, int areaRows)
    {
        if (areaColumns <= 0 || areaRows <= 0)
        {
            return (0, 0);
        }

        if (pictureAspect <= 0)
        {
            pictureAspect = 16.0 / 9.0;
        }

        var columns = areaColumns;
        var rows = (int)Math.Round(columns / pictureAspect * CellAspect);
        if (rows > areaRows)
        {
            rows = areaRows;
            columns = (int)Math.Round(rows * pictureAspect / CellAspect);
        }

        return (Math.Clamp(columns, 1, areaColumns), Math.Max(1, rows));
    }

    /// <summary>Converts <paramref name="rgb"/> (packed, <c>columns × rows × 3</c> bytes) into <paramref name="frame"/>.</summary>
    public static void Render(ReadOnlySpan<byte> rgb, AsciiFrame frame, AsciiColorMode mode)
    {
        var pixels = frame.Columns * frame.Rows;
        if (rgb.Length < pixels * 3)
        {
            throw new ArgumentException("The pixel data is smaller than the frame.", nameof(rgb));
        }

        var last = Ramp.Length - 1;
        for (var i = 0; i < pixels; i++)
        {
            int r = rgb[i * 3];
            int g = rgb[i * 3 + 1];
            int b = rgb[i * 3 + 2];

            // Rec. 601 luma in integers, 0..255.
            var luma = (r * 299 + g * 587 + b * 114) / 1000;
            frame.Characters[i] = Ramp[luma * last / 255];
            frame.Foregrounds[i] = mode == AsciiColorMode.Color
                ? ColorOf(r, g, b)
                : luma >= 128 ? Color.White : Color.Gray;
        }
    }

    /// <summary>The 16-colour console colour nearest to the pixel, judged by which channels are strong.</summary>
    public static Color ColorOf(int r, int g, int b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        if (max < BlackLevel)
        {
            return Color.DarkGray;
        }

        var cutoff = max * HueThreshold;
        var bits = (b >= cutoff ? 1 : 0) | (g >= cutoff ? 2 : 0) | (r >= cutoff ? 4 : 0);

        // ConsoleColor orders the colours as blue=1, green=2, red=4 with +8 for the intense set, so
        // the channel bits are the colour. With every channel present the colour is a grey.
        if (bits == 7)
        {
            return max >= 200 ? Color.White : max >= BrightLevel ? Color.Gray : Color.DarkGray;
        }

        return (Color)(bits | (max >= BrightLevel ? 8 : 0));
    }
}

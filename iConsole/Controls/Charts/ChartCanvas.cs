using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using System.Globalization;

namespace iConsole.Controls.Charts;

/// <summary>
/// A grid of coloured cells that a chart paints into before one write to the buffer. Besides whole cells it has a braille
/// layer with 2x4 dots per cell (<see cref="Dot"/>), so a scatter plot can place points finer than a cell. A cell holds one
/// colour: where dots of two colours share a cell the later one wins.
/// </summary>
public sealed class ChartCanvas
{
    private readonly char[,] _chars;
    private readonly Color?[,] _colors;
    private readonly int[,] _dots;

    public ChartCanvas(int width, int height)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        _chars = new char[Height, Width];
        _colors = new Color?[Height, Width];
        _dots = new int[Height, Width];
        Clear();
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Width in braille dots: two per cell.</summary>
    public int PixelWidth => Width * 2;

    /// <summary>Height in braille dots: four per cell.</summary>
    public int PixelHeight => Height * 4;

    public void Clear()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                _chars[y, x] = ' ';
                _colors[y, x] = null;
                _dots[y, x] = 0;
            }
        }
    }

    /// <summary>Puts one character in a cell; a position outside the canvas is ignored. A null colour is the control's foreground.</summary>
    public void Set(int x, int y, char c, Color? color = null)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
        {
            _chars[y, x] = c;
            _colors[y, x] = color;
            _dots[y, x] = 0;
        }
    }

    /// <summary>Writes text from a cell to the right, clipped at the edge.</summary>
    public void Text(int x, int y, string text, Color? color = null)
    {
        for (int i = 0; i < text.Length; i++)
        {
            Set(x + i, y, text[i], color);
        }
    }

    /// <summary>Turns on one braille dot at dot coordinates (<c>px</c> 0 to <see cref="PixelWidth"/>-1, <c>py</c> 0 to <see cref="PixelHeight"/>-1).</summary>
    public void Dot(int px, int py, Color? color = null)
    {
        if (px < 0 || py < 0 || px >= PixelWidth || py >= PixelHeight)
        {
            return;
        }

        int x = px / 2;
        int y = py / 4;
        _dots[y, x] |= DotBit(px % 2, py % 4);
        _chars[y, x] = (char)(0x2800 + _dots[y, x]);
        _colors[y, x] = color;
    }

    private static int DotBit(int column, int row) => column == 0
        ? row switch { 0 => 0x01, 1 => 0x02, 2 => 0x04, _ => 0x40 }
        : row switch { 0 => 0x08, 1 => 0x10, 2 => 0x20, _ => 0x80 };

    public char CharAt(int x, int y) => _chars[y, x];

    public Color? ColorAt(int x, int y) => _colors[y, x];

    /// <summary>Writes every row to the buffer, one write per row.</summary>
    public void WriteTo(IBuffer buffer, int zOrder, Position origin, Color foreground, Color background)
    {
        for (int y = 0; y < Height; y++)
        {
            var row = new ConxelString(string.Empty, foreground, background);
            for (int x = 0; x < Width; x++)
            {
                row.Append(_chars[y, x], _colors[y, x] ?? foreground, background);
            }

            buffer.Write(zOrder, new Position(origin.X, origin.Y + y), new Size(Width, 1), row);
        }
    }
}

/// <summary>Axis maths shared by the charts: round tick values and their labels.</summary>
public static class ChartAxis
{
    /// <summary>
    /// A range that starts and ends on round numbers, with at most about <paramref name="maxTicks"/> ticks (the step is 1, 2 or 5
    /// times a power of ten). A range of no width (all values equal) is widened so the value sits in the middle.
    /// </summary>
    public static (double Min, double Max, double Step) NiceRange(double min, double max, int maxTicks)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max))
        {
            return (0, 1, 0.5);
        }

        if (max < min)
        {
            (min, max) = (max, min);
        }

        if (max - min < 1e-12)
        {
            double pad = Math.Abs(min) < 1e-12 ? 1 : Math.Abs(min) * 0.1;
            min -= pad;
            max += pad;
        }

        int intervals = Math.Max(1, maxTicks - 1);
        double raw = (max - min) / intervals;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double residual = raw / magnitude;
        double step = (residual <= 1 ? 1 : residual <= 2 ? 2 : residual <= 5 ? 5 : 10) * magnitude;
        double niceMin = Math.Floor(min / step + 1e-9) * step;
        double niceMax = Math.Ceiling(max / step - 1e-9) * step;
        return (niceMin, niceMax, step);
    }

    /// <summary>The tick values from <paramref name="min"/> to <paramref name="max"/>, <paramref name="step"/> apart.</summary>
    public static IReadOnlyList<double> Ticks(double min, double max, double step)
    {
        var ticks = new List<double>();
        if (step <= 0)
        {
            return ticks;
        }

        int count = (int)Math.Round((max - min) / step);
        for (int i = 0; i <= Math.Min(count, 1000); i++)
        {
            ticks.Add(Math.Round(min + (i * step), 10));
        }

        return ticks;
    }

    /// <summary>A tick value with only as many decimals as the step needs ("-0" becomes "0").</summary>
    public static string Format(double value, double step)
    {
        int decimals = step >= 1 || step <= 0 ? 0 : (int)Math.Min(6, Math.Ceiling(-Math.Log10(step) - 1e-9));
        string text = value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        return text.TrimStart('-').Trim('0', '.').Length == 0 ? text.TrimStart('-') : text;
    }
}

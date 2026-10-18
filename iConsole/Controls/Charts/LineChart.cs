using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls.Charts;

/// <summary>
/// Multi-row line plot drawn with braille glyphs (2x4 dots per cell), like Terminal.Gui's graph views.
/// The series is resampled to the dot width, so any number of values fits; consecutive points are joined.
/// </summary>
public class LineChart(IThemeService themeService) : Control(themeService)
{
    private IReadOnlyList<double> _values = [];

    /// <summary>The series to plot.</summary>
    public IReadOnlyList<double> Values
    {
        get => _values;
        set
        {
            _values = value ?? [];
            StateHasChanged();
        }
    }

    /// <summary>Value on the bottom dot row. Null uses the series minimum.</summary>
    public double? Minimum { get; set; }

    /// <summary>Value on the top dot row. Null uses the series maximum.</summary>
    public double? Maximum { get; set; }

    /// <summary>Overrides the theme foreground for the plotted dots.</summary>
    public Color? Foreground { get; set; }

    /// <summary>Renders <paramref name="values"/> into <paramref name="height"/> rows of <paramref name="width"/> braille cells.</summary>
    public static string[] Render(IReadOnlyList<double> values, int width, int height, double? minimum = null, double? maximum = null)
    {
        if (width <= 0 || height <= 0)
        {
            return [];
        }

        int[,] cells = new int[height, width];
        double[] data = values.Where(v => !double.IsNaN(v)).ToArray();
        if (data.Length > 0)
        {
            int pixelWidth = width * 2;
            int pixelHeight = height * 4;
            double min = minimum ?? data.Min();
            double max = maximum ?? data.Max();
            int? previous = null;
            for (int x = 0; x < pixelWidth; x++)
            {
                double position = pixelWidth == 1 ? 0 : x * (data.Length - 1) / (double)(pixelWidth - 1);
                double value = data[(int)Math.Round(position)];
                double ratio = max <= min ? 0.0 : Math.Clamp((value - min) / (max - min), 0.0, 1.0);
                int y = (int)Math.Round((1.0 - ratio) * (pixelHeight - 1));
                int from = previous ?? y;
                for (int fill = Math.Min(from, y); fill <= Math.Max(from, y); fill++)
                {
                    cells[fill / 4, x / 2] |= DotBit(x % 2, fill % 4);
                }

                previous = y;
            }
        }

        string[] rows = new string[height];
        for (int row = 0; row < height; row++)
        {
            char[] line = new char[width];
            for (int column = 0; column < width; column++)
            {
                line[column] = (char)(0x2800 + cells[row, column]);
            }

            rows[row] = new string(line);
        }

        return rows;
    }

    private static int DotBit(int column, int row) => column == 0
        ? row switch { 0 => 0x01, 1 => 0x02, 2 => 0x04, _ => 0x40 }
        : row switch { 0 => 0x08, 1 => 0x10, 2 => 0x20, _ => 0x80 };

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        string[] rows = Render(_values, width, (int)ViewSize.Height, Minimum, Maximum);
        for (int row = 0; row < rows.Length; row++)
        {
            buffer.Write(
                ZOrder,
                new Position(AbsolutePosition.X, AbsolutePosition.Y + row),
                new Size((uint)width, 1),
                new ConxelString(rows[row], Foreground ?? colorScheme.Foreground, colorScheme.Background));
        }
    }
}

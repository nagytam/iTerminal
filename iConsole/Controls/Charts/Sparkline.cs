using iConsole.Buffers;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls.Charts;

/// <summary>
/// One-line trend chart drawn with the eight block-height glyphs (▁▂▃▄▅▆▇█), like Spectre's spark chart.
/// When there are more values than columns the most recent ones are shown.
/// </summary>
public class Sparkline(IThemeService themeService) : Control(themeService)
{
    private static readonly char[] Levels = ['▁', '▂', '▃', '▄', '▅', '▆', '▇', '█'];

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

    /// <summary>Value drawn as the lowest glyph. Null uses the series minimum.</summary>
    public double? Minimum { get; set; }

    /// <summary>Value drawn as the highest glyph. Null uses the series maximum.</summary>
    public double? Maximum { get; set; }

    /// <summary>Overrides the theme foreground for the plotted glyphs.</summary>
    public Color? Foreground { get; set; }

    /// <summary>Maps a value to a glyph index between 0 and 7.</summary>
    public static int LevelFor(double value, double min, double max)
    {
        if (double.IsNaN(value) || max <= min)
        {
            return 0;
        }

        double ratio = Math.Clamp((value - min) / (max - min), 0.0, 1.0);
        return (int)Math.Round(ratio * (Levels.Length - 1));
    }

    /// <summary>Renders <paramref name="values"/> into at most <paramref name="width"/> glyphs.</summary>
    public static string Render(IReadOnlyList<double> values, int width, double? minimum = null, double? maximum = null)
    {
        if (width <= 0 || values.Count == 0)
        {
            return string.Empty;
        }

        IEnumerable<double> window = values.Count > width ? values.Skip(values.Count - width) : values;
        double[] data = window.ToArray();
        double min = minimum ?? data.Min();
        double max = maximum ?? data.Max();
        return new string(data.Select(v => Levels[LevelFor(v, min, max)]).ToArray());
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        string line = Render(_values, width, Minimum, Maximum).PadRight(width);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, new ConxelString(line, Foreground ?? colorScheme.Foreground, colorScheme.Background));
    }
}

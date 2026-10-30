using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls.Charts;

/// <summary>
/// The frame the scatter, histogram and candlestick charts share: an optional legend row on top, a value axis on the left
/// with round tick labels, the plot, and a row of labels under it. A subclass says what range it needs
/// (<see cref="TryGetRange"/>), paints its marks (<see cref="DrawPlot"/>) and names its x labels and legend entries.
/// </summary>
public abstract class XYChart(IThemeService themeService) : Control(themeService)
{
    /// <summary>The colours series take in turn when they name none.</summary>
    public static IReadOnlyList<Color> DefaultPalette { get; } =
        [Color.Cyan, Color.Yellow, Color.Magenta, Color.Green, Color.Red, Color.Blue, Color.White, Color.DarkYellow];

    /// <summary>Draws the value axis with tick labels and the label row under the plot.</summary>
    public bool ShowAxes { get; set; } = true;

    /// <summary>Draws a row of coloured markers and names above the plot.</summary>
    public bool ShowLegend { get; set; }

    /// <summary>Text shown when there is nothing to plot.</summary>
    public string EmptyText
    {
        get => _emptyText ?? UiText.Get("Common.NoData", "No data");
        set => _emptyText = value ?? string.Empty;
    }

    private string? _emptyText;

    /// <summary>The value range to plot; false when there is no data.</summary>
    protected abstract bool TryGetRange(out double xMin, out double xMax, out double yMin, out double yMax);

    /// <summary>
    /// Whether the y range is widened to round numbers. Charts of counts and prices want it; the x range of a scatter plot
    /// does its own.
    /// </summary>
    protected virtual bool NiceYRange => true;

    /// <summary>Marks inside the plot rectangle (<c>x0</c>, <c>y0</c>, <c>w</c>, <c>h</c> in cells of <paramref name="canvas"/>).</summary>
    protected abstract void DrawPlot(ChartCanvas canvas, int x0, int y0, int w, int h, double xMin, double xMax, double yMin, double yMax);

    /// <summary>Labels for the row under the plot, each at a fraction (0 left to 1 right) of the plot width.</summary>
    protected virtual IEnumerable<(double Fraction, string Text)> XLabels(double xMin, double xMax, int plotWidth) => [];

    /// <summary>Marker colour and name of each series.</summary>
    protected virtual IReadOnlyList<(string Name, Color Color)> LegendEntries => [];

    /// <summary>The colour of series <paramref name="index"/>: <paramref name="chosen"/> or the palette's.</summary>
    protected static Color SeriesColor(Color? chosen, int index) => chosen ?? DefaultPalette[index % DefaultPalette.Count];

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        int width = (int)ViewSize.Width;
        int height = (int)ViewSize.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        ColorScheme scheme = ThemeService.CurrentColorScheme;
        var canvas = new ChartCanvas(width, height);
        Compose(canvas, width, height, scheme);
        canvas.WriteTo(buffer, ZOrder, AbsolutePosition, scheme.Foreground, scheme.Background);
    }

    private void Compose(ChartCanvas canvas, int width, int height, ColorScheme scheme)
    {
        IReadOnlyList<(string Name, Color Color)> legend = ShowLegend ? LegendEntries : [];
        int legendRows = legend.Count > 0 && height > 3 ? 1 : 0;
        int labelRows = ShowAxes && height > 2 ? 1 : 0;
        int plotHeight = height - legendRows - labelRows;
        if (!TryGetRange(out double xMin, out double xMax, out double yMin, out double yMax) || plotHeight < 1)
        {
            canvas.Text(0, 0, EmptyText.Length > width ? EmptyText[..width] : EmptyText, scheme.DisabledForeground);
            return;
        }

        double step = 0;
        if (NiceYRange)
        {
            (yMin, yMax, step) = ChartAxis.NiceRange(yMin, yMax, Math.Max(2, (plotHeight / 3) + 1));
        }
        else if (yMax - yMin < 1e-12)
        {
            yMin -= 1;
            yMax += 1;
        }

        IReadOnlyList<double> ticks = step > 0 ? ChartAxis.Ticks(yMin, yMax, step) : [];
        int labelWidth = ShowAxes ? ticks.Select(t => ChartAxis.Format(t, step).Length).DefaultIfEmpty(0).Max() : 0;
        int x0 = ShowAxes ? labelWidth + 1 : 0;
        int plotWidth = width - x0;
        if (plotWidth < 2)
        {
            return;
        }

        if (legendRows > 0)
        {
            DrawLegend(canvas, legend, width, scheme);
        }

        int y0 = legendRows;
        if (ShowAxes)
        {
            for (int row = 0; row < plotHeight; row++)
            {
                canvas.Set(labelWidth, y0 + row, '│', scheme.DisabledForeground);
            }

            foreach (double tick in ticks)
            {
                int row = RowOf(tick, yMin, yMax, plotHeight);
                string text = ChartAxis.Format(tick, step);
                canvas.Text(labelWidth - text.Length, y0 + row, text, scheme.DisabledForeground);
                canvas.Set(labelWidth, y0 + row, '┤', scheme.DisabledForeground);
            }

            if (labelRows > 0)
            {
                DrawXLabels(canvas, x0, y0 + plotHeight, plotWidth, xMin, xMax, scheme);
            }
        }

        DrawPlot(canvas, x0, y0, plotWidth, plotHeight, xMin, xMax, yMin, yMax);
    }

    /// <summary>The row, 0 at the top, that value <paramref name="value"/> falls on in a plot <paramref name="rows"/> high.</summary>
    protected static int RowOf(double value, double yMin, double yMax, int rows)
    {
        if (rows <= 1 || yMax <= yMin)
        {
            return 0;
        }

        double ratio = Math.Clamp((value - yMin) / (yMax - yMin), 0, 1);
        return (int)Math.Round((1 - ratio) * (rows - 1));
    }

    private void DrawXLabels(ChartCanvas canvas, int x0, int y, int plotWidth, double xMin, double xMax, ColorScheme scheme)
    {
        foreach ((double fraction, string text) in XLabels(xMin, xMax, plotWidth))
        {
            if (text.Length == 0)
            {
                continue;
            }

            int centre = x0 + (int)Math.Round(Math.Clamp(fraction, 0, 1) * (plotWidth - 1));
            int start = fraction <= 0 ? x0 : fraction >= 1 ? centre - text.Length + 1 : centre - (text.Length / 2);
            canvas.Text(Math.Max(x0, start), y, text, scheme.DisabledForeground);
        }
    }

    private static void DrawLegend(ChartCanvas canvas, IReadOnlyList<(string Name, Color Color)> entries, int width, ColorScheme scheme)
    {
        int x = 0;
        foreach ((string name, Color color) in entries)
        {
            if (x + 2 + name.Length > width)
            {
                break;
            }

            canvas.Set(x, 0, '■', color);
            canvas.Text(x + 2, 0, name, scheme.Foreground);
            x += name.Length + 4;
        }
    }
}

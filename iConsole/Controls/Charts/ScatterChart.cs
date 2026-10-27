using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls.Charts;

/// <summary>One group of points in a <see cref="ScatterChart"/>.</summary>
public sealed record ScatterSeries(string Name, IReadOnlyList<(double X, double Y)> Points, Color? Color = null);

/// <summary>
/// Points on x and y axes, one braille dot (2x4 per cell) per point, in a colour for each series. Both axes are widened to
/// round numbers and labelled; <see cref="XYChart.ShowLegend"/> names the series. Points that are not finite are skipped.
/// </summary>
public class ScatterChart(IThemeService themeService) : XYChart(themeService)
{
    private IReadOnlyList<ScatterSeries> _series = [];

    public IReadOnlyList<ScatterSeries> Series
    {
        get => _series;
        set
        {
            _series = value ?? [];
            StateHasChanged();
        }
    }

    private IEnumerable<(double X, double Y)> AllPoints =>
        _series.SelectMany(s => s.Points).Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y));

    protected override bool TryGetRange(out double xMin, out double xMax, out double yMin, out double yMax)
    {
        (double X, double Y)[] points = [.. AllPoints];
        if (points.Length == 0)
        {
            xMin = xMax = yMin = yMax = 0;
            return false;
        }

        (xMin, xMax, _) = ChartAxis.NiceRange(points.Min(p => p.X), points.Max(p => p.X), 6);
        yMin = points.Min(p => p.Y);
        yMax = points.Max(p => p.Y);
        return true;
    }

    protected override IReadOnlyList<(string Name, Color Color)> LegendEntries =>
        [.. _series.Select((s, i) => (s.Name, SeriesColor(s.Color, i)))];

    protected override IEnumerable<(double Fraction, string Text)> XLabels(double xMin, double xMax, int plotWidth)
    {
        (double min, double max, double step) = ChartAxis.NiceRange(xMin, xMax, 6);
        foreach (double tick in ChartAxis.Ticks(min, max, step))
        {
            yield return ((tick - min) / (max - min), ChartAxis.Format(tick, step));
        }
    }

    protected override void DrawPlot(ChartCanvas canvas, int x0, int y0, int w, int h, double xMin, double xMax, double yMin, double yMax)
    {
        int pixelWidth = w * 2;
        int pixelHeight = h * 4;
        for (int s = 0; s < _series.Count; s++)
        {
            Color color = SeriesColor(_series[s].Color, s);
            foreach ((double x, double y) in _series[s].Points)
            {
                if (!double.IsFinite(x) || !double.IsFinite(y))
                {
                    continue;
                }

                double rx = xMax > xMin ? Math.Clamp((x - xMin) / (xMax - xMin), 0, 1) : 0.5;
                double ry = yMax > yMin ? Math.Clamp((y - yMin) / (yMax - yMin), 0, 1) : 0.5;
                canvas.Dot((x0 * 2) + (int)Math.Round(rx * (pixelWidth - 1)), (y0 * 4) + (int)Math.Round((1 - ry) * (pixelHeight - 1)), color);
            }
        }
    }
}

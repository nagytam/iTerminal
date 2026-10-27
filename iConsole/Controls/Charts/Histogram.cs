using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls.Charts;

/// <summary>
/// The distribution of a set of values: they are counted into equal-width bins and each bin is drawn as a bar, with eighth-block
/// tops for a finer height. <see cref="BinCount"/> 0 picks Sturges' rule (log2 n + 1). The vertical axis is the count; the row
/// under the plot labels the lowest, middle and highest value.
/// </summary>
public class Histogram(IThemeService themeService) : XYChart(themeService)
{
    private static readonly char[] Eighths = [' ', '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█'];

    private IReadOnlyList<double> _values = [];

    public IReadOnlyList<double> Values
    {
        get => _values;
        set
        {
            _values = value ?? [];
            StateHasChanged();
        }
    }

    /// <summary>The number of bins; 0 chooses by Sturges' rule. Never more than the plot has columns.</summary>
    public int BinCount { get; set; }

    /// <summary>The colour of the bars; the first palette colour when null.</summary>
    public Color? BarColor { get; set; }

    /// <summary>Counts <paramref name="values"/> into <paramref name="bins"/> equal bins from the smallest to the largest value (the largest falls in the last bin). Values that are not finite are skipped.</summary>
    public static int[] Bin(IEnumerable<double> values, int bins)
    {
        double[] data = [.. values.Where(double.IsFinite)];
        bins = Math.Max(1, bins);
        int[] counts = new int[bins];
        if (data.Length == 0)
        {
            return counts;
        }

        double min = data.Min();
        double max = data.Max();
        foreach (double value in data)
        {
            int bin = max <= min ? 0 : (int)((value - min) / (max - min) * bins);
            counts[Math.Min(bin, bins - 1)]++;
        }

        return counts;
    }

    /// <summary>Sturges' rule: the number of bins for <paramref name="count"/> values.</summary>
    public static int SturgesBins(int count) => count <= 1 ? 1 : (int)Math.Ceiling(Math.Log2(count)) + 1;

    private double[] Data => [.. _values.Where(double.IsFinite)];

    private int BinsFor(int plotWidth, int count)
    {
        int wanted = BinCount > 0 ? BinCount : SturgesBins(count);
        return Math.Clamp(wanted, 1, Math.Max(1, plotWidth));
    }

    protected override bool TryGetRange(out double xMin, out double xMax, out double yMin, out double yMax)
    {
        double[] data = Data;
        if (data.Length == 0)
        {
            xMin = xMax = yMin = yMax = 0;
            return false;
        }

        xMin = data.Min();
        xMax = data.Max();
        yMin = 0;
        // The tallest bin depends on the plot width, which is not known here; the widest the bins can be is BinCount or Sturges.
        yMax = Bin(data, BinCount > 0 ? BinCount : SturgesBins(data.Length)).Max();
        return true;
    }

    protected override IEnumerable<(double Fraction, string Text)> XLabels(double xMin, double xMax, int plotWidth)
    {
        double step = Math.Max(1e-9, (xMax - xMin) / 4);
        yield return (0, ChartAxis.Format(xMin, step));
        if (plotWidth >= 20)
        {
            yield return (0.5, ChartAxis.Format((xMin + xMax) / 2, step));
        }

        yield return (1, ChartAxis.Format(xMax, step));
    }

    protected override void DrawPlot(ChartCanvas canvas, int x0, int y0, int w, int h, double xMin, double xMax, double yMin, double yMax)
    {
        double[] data = Data;
        int bins = BinsFor(w, data.Length);
        int[] counts = Bin(data, bins);
        // The y range was niced for the unclamped bin count; scale by the larger of that and what is drawn.
        double top = Math.Max(yMax, counts.Max());
        Color color = BarColor ?? DefaultPalette[0];
        for (int bin = 0; bin < bins; bin++)
        {
            int from = bin * w / bins;
            int to = (bin + 1) * w / bins;
            // A gap column between wide bars keeps neighbours apart.
            int last = to - from >= 3 ? to - 2 : to - 1;
            int eighths = (int)Math.Round(counts[bin] / top * h * 8);
            if (counts[bin] > 0 && eighths == 0)
            {
                eighths = 1;
            }

            for (int x = from; x <= last; x++)
            {
                for (int row = 0; row < h; row++)
                {
                    int cellEighths = Math.Clamp(eighths - (row * 8), 0, 8);
                    if (cellEighths > 0)
                    {
                        canvas.Set(x0 + x, y0 + h - 1 - row, Eighths[cellEighths], color);
                    }
                }
            }
        }
    }
}

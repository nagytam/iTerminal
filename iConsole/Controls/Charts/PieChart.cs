using iConsole.Buffers;
using iConsole.Display;
using iConsole.Services;
using System.Globalization;

namespace iConsole.Controls.Charts;

/// <summary>One wedge of a <see cref="PieChart"/>. Values below zero and not finite count as zero.</summary>
public sealed record ChartSlice(string Label, double Value, Color? Color = null);

/// <summary>
/// A pie, or a donut with <see cref="InnerRadius"/> above 0, drawn with block cells. Wedges run clockwise from twelve o'clock in
/// the order given, and a cell takes the colour of the wedge at its centre. A terminal cell is about twice as tall as wide, so
/// the circle is sized by that and stays round. <see cref="ShowLegend"/> lists each label with its share on the right.
/// </summary>
public class PieChart(IThemeService themeService) : Control(themeService)
{
    private IReadOnlyList<ChartSlice> _slices = [];
    private double _innerRadius;

    public IReadOnlyList<ChartSlice> Slices
    {
        get => _slices;
        set
        {
            _slices = value ?? [];
            StateHasChanged();
        }
    }

    /// <summary>The hole as a share of the radius: 0 is a pie, 0.5 a donut. Held between 0 and 0.9.</summary>
    public double InnerRadius
    {
        get => _innerRadius;
        set
        {
            _innerRadius = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 0.9);
            StateHasChanged();
        }
    }

    public bool ShowLegend { get; set; } = true;

    public string EmptyText { get; set; } = "No data";

    private static double Weight(ChartSlice slice) => double.IsFinite(slice.Value) && slice.Value > 0 ? slice.Value : 0;

    /// <summary>The share of each slice, 0 to 1; all zero when the values total nothing.</summary>
    public static double[] Shares(IReadOnlyList<ChartSlice> slices)
    {
        double total = slices.Sum(Weight);
        return [.. slices.Select(s => total > 0 ? Weight(s) / total : 0)];
    }

    /// <summary>The index of the slice that holds <paramref name="fraction"/> (0 to 1 clockwise from the top), or -1 when none has a share.</summary>
    public static int SliceAt(IReadOnlyList<ChartSlice> slices, double fraction)
    {
        double[] shares = Shares(slices);
        double cumulative = 0;
        int last = -1;
        for (int i = 0; i < shares.Length; i++)
        {
            if (shares[i] <= 0)
            {
                continue;
            }

            last = i;
            cumulative += shares[i];
            if (fraction < cumulative)
            {
                return i;
            }
        }

        return last;
    }

    private static string Percent(double share) => share > 0 && share < 0.005 ? "<1%" : (share * 100).ToString("F0", CultureInfo.InvariantCulture) + "%";

    private Color ColorOf(int index) => _slices[index].Color ?? XYChart.DefaultPalette[index % XYChart.DefaultPalette.Count];

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
        double[] shares = Shares(_slices);
        if (shares.All(s => s <= 0))
        {
            canvas.Text(0, 0, EmptyText.Length > width ? EmptyText[..width] : EmptyText, scheme.DisabledForeground);
        }
        else
        {
            int legendWidth = 0;
            if (ShowLegend)
            {
                legendWidth = Math.Min(width / 2, _slices.Select((s, i) => 2 + s.Label.Length + 2 + Percent(shares[i]).Length).Max());
            }

            int plotWidth = Math.Max(0, width - legendWidth - (legendWidth > 0 ? 1 : 0));
            DrawPie(canvas, plotWidth, height);
            if (legendWidth > 0)
            {
                DrawLegend(canvas, plotWidth + 1, legendWidth, height, shares, scheme);
            }
        }

        canvas.WriteTo(buffer, ZOrder, AbsolutePosition, scheme.Foreground, scheme.Background);
    }

    private void DrawPie(ChartCanvas canvas, int plotWidth, int height)
    {
        // Cell centres are (column + 0.5) / 2 across and (row + 0.5) down, so that one unit is the same distance both ways.
        double radius = Math.Min(height / 2.0, plotWidth / 4.0);
        if (radius < 0.5)
        {
            return;
        }

        double centreX = plotWidth / 4.0;
        double centreY = height / 2.0;
        // Centre the circle in the free space.
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < plotWidth; column++)
            {
                double dx = ((column + 0.5) / 2.0) - centreX;
                double dy = (row + 0.5) - centreY;
                double distance = Math.Sqrt((dx * dx) + (dy * dy));
                if (distance > radius || distance < radius * _innerRadius)
                {
                    continue;
                }

                double angle = Math.Atan2(dx, -dy);
                double fraction = (angle < 0 ? angle + (2 * Math.PI) : angle) / (2 * Math.PI);
                int slice = SliceAt(_slices, fraction);
                if (slice >= 0)
                {
                    canvas.Set(column, row, '█', ColorOf(slice));
                }
            }
        }
    }

    private void DrawLegend(ChartCanvas canvas, int x, int legendWidth, int height, double[] shares, ColorScheme scheme)
    {
        for (int i = 0; i < _slices.Count && i < height; i++)
        {
            string percent = Percent(shares[i]);
            int labelRoom = Math.Max(0, legendWidth - 2 - 1 - percent.Length);
            string label = _slices[i].Label.Length > labelRoom ? _slices[i].Label[..labelRoom] : _slices[i].Label;
            canvas.Set(x, i, '■', ColorOf(i));
            canvas.Text(x + 2, i, label, scheme.Foreground);
            canvas.Text(x + legendWidth - percent.Length, i, percent, scheme.DisabledForeground);
        }
    }
}

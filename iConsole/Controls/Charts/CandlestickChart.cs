using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls.Charts;

/// <summary>One period of a <see cref="CandlestickChart"/>: where a price opened, how high and low it went, and where it closed.</summary>
public sealed record Candle(string Label, double Open, double High, double Low, double Close);

/// <summary>
/// Open-high-low-close candles, oldest on the left. The body (open to close) is a block, the wick (low to high) a thin line;
/// <see cref="UpColor"/> is used when the price closed at or above its open and <see cref="DownColor"/> when it closed lower.
/// A candle takes two columns (one when they do not fit), and when there are more candles than fit only the newest are drawn.
/// The vertical range covers every candle so it does not shift as the chart is resized.
/// </summary>
public class CandlestickChart(IThemeService themeService) : XYChart(themeService)
{
    private IReadOnlyList<Candle> _candles = [];

    public IReadOnlyList<Candle> Candles
    {
        get => _candles;
        set
        {
            _candles = value ?? [];
            StateHasChanged();
        }
    }

    public Color UpColor { get; set; } = Color.Green;

    public Color DownColor { get; set; } = Color.Red;

    private IEnumerable<Candle> Valid => _candles.Where(c =>
        double.IsFinite(c.Open) && double.IsFinite(c.High) && double.IsFinite(c.Low) && double.IsFinite(c.Close));

    protected override bool TryGetRange(out double xMin, out double xMax, out double yMin, out double yMax)
    {
        Candle[] candles = [.. Valid];
        xMin = 0;
        xMax = Math.Max(0, candles.Length - 1);
        if (candles.Length == 0)
        {
            yMin = yMax = 0;
            return false;
        }

        yMin = candles.Min(c => Math.Min(c.Low, Math.Min(c.Open, c.Close)));
        yMax = candles.Max(c => Math.Max(c.High, Math.Max(c.Open, c.Close)));
        return true;
    }

    protected override IReadOnlyList<(string Name, Color Color)> LegendEntries => [("Up", UpColor), ("Down", DownColor)];

    private static int ColumnsPerCandle(int plotWidth, int count) => plotWidth >= count * 2 ? 2 : 1;

    private Candle[] Visible(int plotWidth)
    {
        Candle[] candles = [.. Valid];
        int perCandle = ColumnsPerCandle(plotWidth, candles.Length);
        int fit = Math.Max(1, plotWidth / perCandle);
        return candles.Length <= fit ? candles : candles[^fit..];
    }

    protected override IEnumerable<(double Fraction, string Text)> XLabels(double xMin, double xMax, int plotWidth)
    {
        Candle[] visible = Visible(plotWidth);
        if (visible.Length == 0)
        {
            yield break;
        }

        yield return (0, visible[0].Label);
        if (visible.Length > 1)
        {
            yield return (1, visible[^1].Label);
        }
    }

    protected override void DrawPlot(ChartCanvas canvas, int x0, int y0, int w, int h, double xMin, double xMax, double yMin, double yMax)
    {
        Candle[] visible = Visible(w);
        int perCandle = ColumnsPerCandle(w, Valid.Count());
        for (int i = 0; i < visible.Length; i++)
        {
            Candle candle = visible[i];
            int x = x0 + (i * perCandle);
            Color color = candle.Close >= candle.Open ? UpColor : DownColor;
            int highRow = RowOf(candle.High, yMin, yMax, h);
            int lowRow = RowOf(candle.Low, yMin, yMax, h);
            for (int row = highRow; row <= lowRow; row++)
            {
                canvas.Set(x, y0 + row, '│', color);
            }

            int openRow = RowOf(candle.Open, yMin, yMax, h);
            int closeRow = RowOf(candle.Close, yMin, yMax, h);
            for (int row = Math.Min(openRow, closeRow); row <= Math.Max(openRow, closeRow); row++)
            {
                canvas.Set(x, y0 + row, '█', color);
            }
        }
    }
}

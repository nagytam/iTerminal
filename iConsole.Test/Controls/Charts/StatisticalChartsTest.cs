using iConsole.Controls.Charts;
using Control = iConsole.Controls.Control;
using iConsole.Data;
using iConsole.Display;
using iConsole.Test.Core;

namespace iConsole.Test.Controls.Charts;

[TestClass]
public class StatisticalChartsTest
{
    private readonly TestThemeService _theme = new();

    private static ScreenBuffer Draw(Control chart, int width, int height)
    {
        chart.ViewSize = new Size(width, height);
        var screen = new ScreenBuffer(width, height);
        chart.Draw(screen);
        chart.Draw(screen);
        return screen;
    }

    // ---- axis maths ----

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(0, 100, 5, 0, 100, 50)]
    [DataRow(0, 9, 5, 0, 10, 5)]
    [DataRow(3, 97, 6, 0, 100, 20)]
    [DataRow(-5, 5, 3, -5, 5, 5)]
    public void NiceRange_EndsOnRoundNumbers(double min, double max, int ticks, double niceMin, double niceMax, double step)
    {
        (double lo, double hi, double s) = ChartAxis.NiceRange(min, max, ticks);

        Assert.AreEqual(niceMin, lo, 1e-9);
        Assert.AreEqual(niceMax, hi, 1e-9);
        Assert.AreEqual(step, s, 1e-9);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NiceRange_WidensAZeroWidthRangeAndSurvivesNonFiniteInput()
    {
        (double lo, double hi, _) = ChartAxis.NiceRange(5, 5, 4);
        Assert.IsTrue(lo < 5 && hi > 5);

        (double a, double b, double step) = ChartAxis.NiceRange(double.NaN, 1, 4);
        Assert.IsTrue(a < b && step > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Format_UsesOnlyTheDecimalsTheStepNeeds()
    {
        Assert.AreEqual("5", ChartAxis.Format(5, 5));
        Assert.AreEqual("0.5", ChartAxis.Format(0.5, 0.5));
        Assert.AreEqual("0.25", ChartAxis.Format(0.25, 0.05));
        Assert.AreEqual("0", ChartAxis.Format(-0.0001, 1));
        CollectionAssert.AreEqual(new[] { 0.0, 2.5, 5.0 }, ChartAxis.Ticks(0, 5, 2.5).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Canvas_IgnoresOutOfRangeWritesAndCombinesBrailleDots()
    {
        var canvas = new ChartCanvas(2, 1);
        canvas.Set(-1, 0, 'x');
        canvas.Set(9, 9, 'x');
        canvas.Text(1, 0, "abc");
        canvas.Dot(0, 0);
        canvas.Dot(1, 3);

        Assert.AreEqual((char)(0x2800 + 0x01 + 0x80), canvas.CharAt(0, 0));
        Assert.AreEqual('a', canvas.CharAt(1, 0));
        canvas.Dot(-1, 0);
        canvas.Dot(4, 0);
        Assert.AreEqual('a', canvas.CharAt(1, 0));
    }

    // ---- pie ----

    private static IReadOnlyList<ChartSlice> Halves => [new("A", 1, Color.Red), new("B", 1, Color.Blue)];

    [Timeout(60_000)]
    [TestMethod]
    public void Pie_SharesAreFractionsOfThePositiveTotal()
    {
        double[] shares = PieChart.Shares([new("a", 1), new("b", 3), new("neg", -5), new("nan", double.NaN)]);

        CollectionAssert.AreEqual(new[] { 0.25, 0.75, 0.0, 0.0 }, shares);
        CollectionAssert.AreEqual(new[] { 0.0 }, PieChart.Shares([new("z", 0)]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Pie_SliceAt_RunsClockwiseFromTheTop()
    {
        Assert.AreEqual(0, PieChart.SliceAt(Halves, 0.0));
        Assert.AreEqual(0, PieChart.SliceAt(Halves, 0.49));
        Assert.AreEqual(1, PieChart.SliceAt(Halves, 0.5));
        Assert.AreEqual(1, PieChart.SliceAt(Halves, 0.999));
        Assert.AreEqual(-1, PieChart.SliceAt([new("z", 0)], 0.3));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Pie_DrawsTheFirstSliceOnTheRightAndTheSecondOnTheLeft()
    {
        var chart = new PieChart(_theme) { Slices = Halves, ShowLegend = false };

        ScreenBuffer screen = Draw(chart, 20, 10);

        Assert.AreEqual(Color.Red, screen.ForegroundAt(14, 5));
        Assert.AreEqual(Color.Blue, screen.ForegroundAt(6, 5));
        Assert.AreEqual(' ', screen.CharAt(0, 0), "outside the circle stays empty");
        Assert.AreEqual(' ', screen.CharAt(19, 9));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Donut_HasAnEmptyHole()
    {
        var chart = new PieChart(_theme) { Slices = Halves, ShowLegend = false, InnerRadius = 0.5 };

        ScreenBuffer screen = Draw(chart, 20, 10);

        Assert.AreEqual(' ', screen.CharAt(10, 5));
        Assert.AreEqual('█', screen.CharAt(17, 5));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Pie_LegendListsLabelsWithSharesAndTheCirclesStaysLeftOfIt()
    {
        var chart = new PieChart(_theme) { Slices = [new("Docs", 3, Color.Cyan), new("Code", 1, Color.Green)] };

        ScreenBuffer screen = Draw(chart, 40, 8);

        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(screen.Line(0), "Docs");
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(screen.Line(0), "75%");
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(screen.Line(1), "25%");
        Assert.AreEqual(Color.Cyan, screen.ForegroundAt(screen.Line(0).IndexOf('■'), 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Pie_EmptyOrTinyDataDoesNotThrow()
    {
        var chart = new PieChart(_theme);
        Assert.IsTrue(Draw(chart, 20, 5).Contains("No data"));

        chart.Slices = Halves;
        Draw(chart, 1, 1);
        Draw(chart, 3, 2);
        chart.InnerRadius = double.NaN;
        Assert.AreEqual(0, chart.InnerRadius);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Pie_TinyShareReadsLessThanOnePercent()
    {
        var chart = new PieChart(_theme) { Slices = [new("big", 1000), new("tiny", 1)] };

        Assert.IsTrue(Draw(chart, 40, 6).Contains("<1%"));
    }

    // ---- scatter ----

    private static IReadOnlyList<ScatterSeries> TwoSeries => new[]
    {
        new ScatterSeries("up", [(0, 0), (10, 10)], Color.Green),
        new ScatterSeries("down", [(0, 10), (10, 0)], Color.Red),
    };

    [Timeout(60_000)]
    [TestMethod]
    public void Scatter_PlotsDotsInTheColourOfTheirSeries()
    {
        var chart = new ScatterChart(_theme) { Series = TwoSeries, ShowAxes = false };

        ScreenBuffer screen = Draw(chart, 20, 8);

        // (0,0) is the bottom-left dot and (10,10) the top-right.
        Assert.AreNotEqual(' ', screen.CharAt(0, 7));
        Assert.AreNotEqual(' ', screen.CharAt(19, 0));
        Assert.AreEqual(Color.Red, screen.ForegroundAt(0, 0), "(0,10) is top-left, drawn by the second series");
        Assert.AreEqual(Color.Red, screen.ForegroundAt(19, 7));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Scatter_AxesShowRoundTicksAndTheLegendNamesSeries()
    {
        var chart = new ScatterChart(_theme) { Series = TwoSeries, ShowLegend = true };

        ScreenBuffer screen = Draw(chart, 40, 10);

        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(screen.Line(0), "up");
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(screen.Line(0), "down");
        Assert.IsTrue(screen.Lines.Any(l => l.StartsWith("10 ┤") || l.TrimStart().StartsWith("10")), "top tick");
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(screen.Line(9), "10");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Scatter_IgnoresNonFinitePointsAndSinglePoints()
    {
        var chart = new ScatterChart(_theme)
        {
            Series = [new("s", [(double.NaN, 1), (1, double.PositiveInfinity), (5, 5)])],
        };

        ScreenBuffer screen = Draw(chart, 30, 8);

        Assert.IsFalse(screen.Contains("No data"));
        Assert.IsTrue(Draw(new ScatterChart(_theme), 30, 8).Contains("No data"));
    }

    // ---- histogram ----

    [Timeout(60_000)]
    [TestMethod]
    public void Histogram_BinCountsEveryFiniteValueOnce()
    {
        int[] counts = Histogram.Bin([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, double.NaN], 5);

        Assert.AreEqual(11, counts.Sum());
        CollectionAssert.AreEqual(new[] { 2, 2, 2, 2, 3 }, counts);
        CollectionAssert.AreEqual(new[] { 3 }, Histogram.Bin([4, 4, 4], 1));
        CollectionAssert.AreEqual(new[] { 3, 0 }, Histogram.Bin([4, 4, 4], 2));
        Assert.AreEqual(0, Histogram.Bin([], 3).Sum());
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 1)]
    [DataRow(8, 4)]
    [DataRow(100, 8)]
    public void Histogram_SturgesRule(int count, int bins)
    {
        Assert.AreEqual(bins, Histogram.SturgesBins(count));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Histogram_TallerBinsDrawTallerBars()
    {
        var chart = new Histogram(_theme) { Values = [1, 1, 1, 1, 1, 1, 1, 1, 9], BinCount = 2, ShowAxes = false };

        ScreenBuffer screen = Draw(chart, 20, 8);

        Assert.AreEqual('█', screen.CharAt(0, 2), "the tall bar is full height up to its top cell");
        Assert.AreEqual(' ', screen.CharAt(0, 0));
        Assert.AreEqual(' ', screen.CharAt(15, 2));
        Assert.AreNotEqual(' ', screen.CharAt(15, 7), "the short bar still shows at the bottom");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Histogram_LabelsTheLowestAndHighestValueAndHandlesNoData()
    {
        var chart = new Histogram(_theme) { Values = [10, 20, 30, 40] };

        ScreenBuffer screen = Draw(chart, 40, 8);

        string last = screen.Line(7);
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(last, "10");
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(last, "40");
        Assert.IsTrue(Draw(new Histogram(_theme), 20, 5).Contains("No data"));
    }

    // ---- candlestick ----

    private static IReadOnlyList<Candle> Candles =>
    [
        new("Mon", 10, 14, 9, 13),
        new("Tue", 13, 15, 8, 9),
    ];

    [Timeout(60_000)]
    [TestMethod]
    public void Candlestick_ColoursUpAndDownCandles()
    {
        var chart = new CandlestickChart(_theme) { Candles = Candles, ShowAxes = false };

        ScreenBuffer screen = Draw(chart, 10, 10);

        int rowOfFirst = Enumerable.Range(0, 10).First(r => screen.CharAt(0, r) != ' ');
        Assert.AreEqual(Color.Green, screen.ForegroundAt(0, rowOfFirst));
        int rowOfSecond = Enumerable.Range(0, 10).First(r => screen.CharAt(2, r) != ' ');
        Assert.AreEqual(Color.Red, screen.ForegroundAt(2, rowOfSecond));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Candlestick_BodyIsBlockAndWickIsALine()
    {
        var chart = new CandlestickChart(_theme) { Candles = [new("a", 10, 20, 0, 12)], ShowAxes = false };

        ScreenBuffer screen = Draw(chart, 4, 11);

        Assert.AreEqual('│', screen.CharAt(0, 0), "wick above the body");
        Assert.AreEqual('│', screen.CharAt(0, 10), "wick below the body");
        Assert.IsTrue(Enumerable.Range(0, 11).Any(r => screen.CharAt(0, r) == '█'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Candlestick_WithMoreCandlesThanFit_DrawsTheNewest()
    {
        IReadOnlyList<Candle> many = [.. Enumerable.Range(0, 30).Select(i => new Candle($"d{i}", i, i + 2, i - 1, i + 1))];
        var chart = new CandlestickChart(_theme) { Candles = many };

        ScreenBuffer screen = Draw(chart, 12, 8);

        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(screen.Line(7), "d29");
        Assert.IsFalse(screen.Contains("d0"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Candlestick_LegendNamesUpAndDown_AndBadCandlesAreSkipped()
    {
        var chart = new CandlestickChart(_theme)
        {
            Candles = [.. Candles, new Candle("bad", double.NaN, 1, 1, 1)],
            ShowLegend = true,
        };

        ScreenBuffer screen = Draw(chart, 30, 10);

        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(screen.Line(0), "Up");
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(screen.Line(0), "Down");
        Assert.IsFalse(screen.Contains("bad"));
        Assert.IsTrue(Draw(new CandlestickChart(_theme), 20, 5).Contains("No data"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Charts_FillTheirBoundsOnRedrawAndSurviveTinySizes()
    {
        Control[] charts =
        [
            new PieChart(_theme) { Slices = Halves },
            new ScatterChart(_theme) { Series = TwoSeries, ShowLegend = true },
            new Histogram(_theme) { Values = [1, 2, 2, 3, 3, 3] },
            new CandlestickChart(_theme) { Candles = Candles, ShowLegend = true },
        ];

        foreach (Control chart in charts)
        {
            foreach ((int w, int h) in new[] { (40, 12), (12, 4), (5, 2), (1, 1), (3, 1) })
            {
                Draw(chart, w, h);
            }

            ScreenBuffer screen = Draw(chart, 40, 12);
            screen.AssertFillsBounds(chart);
        }
    }
}

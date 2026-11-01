using iConsole.Controls;
using iConsole.Controls.Charts;
using iConsole.Data;
using iConsole.Display;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

/// <summary>Charts that threw or drew garbage for NaN, infinity, null labels or a bad format string.</summary>
[TestClass]
public class ControlsReviewBatch26Test
{
    private readonly TestThemeService _theme = new();

    [Timeout(60_000)]
    [TestMethod]
    public void BarChart_BuildBar_GivesNaNAnEmptyBar()
    {
        Assert.AreEqual("    ", BarChart.BuildBar(double.NaN, 4));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BarChart_ANaNValue_DrawsItsOtherBars()
    {
        BarChart chart = new(_theme) { Position = new Position(0, 0), ViewSize = new Size(30, 2) };
        chart.Items = [new BarChartItem("bad", double.NaN), new BarChartItem("good", 4)];

        ScreenBuffer screen = ScreenBuffer.Render(chart, 30, 2);

        Assert.IsTrue(screen.Line(1).Contains("good"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BarChart_ABadFormatString_FallsBackToTheDefault()
    {
        BarChart chart = new(_theme) { Position = new Position(0, 0), ViewSize = new Size(30, 1), ValueFormat = "Q3" };
        chart.Items = [new BarChartItem("a", 2.5)];

        ScreenBuffer screen = ScreenBuffer.Render(chart, 30, 1);

        Assert.IsTrue(screen.Line(0).Contains("2"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BarChart_NullFormatAndNullLabel_AreAccepted()
    {
        BarChart chart = new(_theme) { Position = new Position(0, 0), ViewSize = new Size(30, 1), ValueFormat = null! };
        chart.Items = [new BarChartItem(null!, 1)];

        Assert.AreEqual("0.##", chart.ValueFormat);
        ScreenBuffer.Render(chart, 30, 1);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Gauge_NullLabel_DrawsWithoutOne()
    {
        Gauge gauge = new(_theme) { Position = new Position(0, 0), ViewSize = new Size(20, 1), Label = null! };

        Assert.AreEqual(string.Empty, gauge.Label);
        ScreenBuffer.Render(gauge, 20, 1);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BreakdownChart_InfinityAndNaN_DoNotTakeTheShareOfTheOthers()
    {
        int[] widths = BreakdownChart.Allocate([double.PositiveInfinity, double.NaN, 1, 1], 10);

        CollectionAssert.AreEqual(new[] { 0, 0, 5, 5 }, widths);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Sparkline_ANaNInTheData_StillScalesTheRest()
    {
        string line = Sparkline.Render([1, double.NaN, 5], 3);

        Assert.AreEqual(3, line.Length);
        Assert.AreNotEqual(line[0], line[2]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Sparkline_Infinity_ClampsToTheEndGlyphs()
    {
        Assert.AreEqual(7, Sparkline.LevelFor(double.PositiveInfinity, 0, 10));
        Assert.AreEqual(0, Sparkline.LevelFor(double.NegativeInfinity, 0, 10));
        Assert.AreEqual(0, Sparkline.LevelFor(3, double.NaN, 10));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LineChart_InfinityInTheData_IsLeftOut()
    {
        string[] rows = LineChart.Render([1, double.PositiveInfinity, 3], 6, 2);

        Assert.AreEqual(2, rows.Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Heatmap_NullRowsAndLabels_AreTakenAsEmpty()
    {
        Heatmap map = new(_theme) { Position = new Position(0, 0), ViewSize = new Size(20, 3) };
        map.Values = [null!, [0.5, 1.0]];
        map.RowLabels = [null!, "b"];
        map.ColumnLabels = [null!, "y"];

        ScreenBuffer.Render(map, 20, 3);
    }
}

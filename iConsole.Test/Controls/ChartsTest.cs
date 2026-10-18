using iConsole.Controls.Charts;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class ChartsTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static string[] Lines(MockBuffer buffer) => buffer.ContentAsString.Split(Environment.NewLine);

    [TestMethod]
    public void Sparkline_Render_ScalesBetweenLowestAndHighestGlyph()
    {
        Assert.AreEqual("▁█", Sparkline.Render([1, 9], 10));
        Assert.AreEqual("▁▅█", Sparkline.Render([0, 5, 10], 10));
    }

    [TestMethod]
    public void Sparkline_Render_KeepsMostRecentValuesWhenTooWide()
    {
        Assert.AreEqual("▁█", Sparkline.Render([5, 5, 0, 9], 2));
    }

    [TestMethod]
    public void Sparkline_Render_FlatSeriesDrawsLowestGlyph()
    {
        Assert.AreEqual("▁▁▁", Sparkline.Render([4, 4, 4], 5));
    }

    [TestMethod]
    public void Sparkline_Draw_PadsToWidth()
    {
        MockBuffer buffer = new(new Size(6, 1));
        new Sparkline(Theme()) { Values = [1, 2], Position = new Position(0, 0), ViewSize = new Size(6, 1) }.Draw(buffer);
        Assert.AreEqual("▁█    ", Lines(buffer)[0]);
    }

    [TestMethod]
    public void BarChart_BuildBar_UsesEighthCellResolution()
    {
        Assert.AreEqual("█▌   ", BarChart.BuildBar(0.3, 5));
        Assert.AreEqual("█████", BarChart.BuildBar(1.0, 5));
        Assert.AreEqual("     ", BarChart.BuildBar(0.0, 5));
    }

    [TestMethod]
    public void BarChart_Draw_AlignsLabelsAndShowsValues()
    {
        MockBuffer buffer = new(new Size(14, 2));
        new BarChart(Theme())
        {
            Items = [new BarChartItem("a", 10), new BarChartItem("bb", 5)],
            Position = new Position(0, 0),
            ViewSize = new Size(14, 2)
        }.Draw(buffer);

        string[] lines = Lines(buffer);
        Assert.AreEqual("a  ████████ 10", lines[0]);
        Assert.AreEqual("bb ████      5", lines[1]);
    }

    [TestMethod]
    public void BreakdownChart_Allocate_AlwaysFillsTheWidth()
    {
        int[] widths = BreakdownChart.Allocate([1, 1, 1], 10);
        Assert.AreEqual(10, widths.Sum());
        CollectionAssert.AreEqual(new[] { 0, 0, 0 }, BreakdownChart.Allocate([0, -1, 0], 10));
    }

    [TestMethod]
    public void BreakdownChart_Draw_ShowsBarAndLegend()
    {
        MockBuffer buffer = new(new Size(20, 3));
        new BreakdownChart(Theme())
        {
            Items = [new BreakdownChartItem("Ok", 3, Color.Green), new BreakdownChartItem("Bad", 1, Color.Red)],
            Position = new Position(0, 0),
            ViewSize = new Size(20, 3)
        }.Draw(buffer);

        string[] lines = Lines(buffer);
        Assert.AreEqual("████████████████████", lines[0]);
        Assert.IsTrue(lines[1].StartsWith("■ Ok 3 (75%)"));
        Assert.IsTrue(lines[2].StartsWith("■ Bad 1 (25%)"));
    }
}

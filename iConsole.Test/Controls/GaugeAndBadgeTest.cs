using iConsole.Controls;
using iConsole.Controls.Charts;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class GaugeAndBadgeTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Gauge_SeverityColor_FollowsThresholds()
    {
        Assert.AreEqual(Color.Green, Gauge.SeverityColor(0.5));
        Assert.AreEqual(Color.Yellow, Gauge.SeverityColor(0.8));
        Assert.AreEqual(Color.Red, Gauge.SeverityColor(0.95));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Gauge_Draw_ShowsLabelBarAndPercent()
    {
        MockBuffer buffer = new(new Size(20, 1));
        new Gauge(Theme()) { Label = "Mem", Value = 0.5, Position = new Position(0, 0), ViewSize = new Size(20, 1) }.Draw(buffer);
        string line = buffer.ContentAsString.Split(Environment.NewLine)[0];
        Assert.IsTrue(line.StartsWith("Mem "));
        Assert.IsTrue(line.EndsWith(" 50%"), "[" + line + "]");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Gauge_Value_IsClamped()
    {
        Gauge gauge = new(Theme()) { Value = 3 };
        Assert.AreEqual(1.0, gauge.Value);
        gauge.Value = double.NaN;
        Assert.AreEqual(0.0, gauge.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Badge_Width_FollowsText()
    {
        Badge badge = new(Theme()) { Text = "PARTIAL" };
        Assert.AreEqual(9, (int)badge.ViewSize.Width);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Badge_Draw_PadsTextWithOneCell()
    {
        MockBuffer buffer = new(new Size(6, 1));
        new Badge(Theme()) { Text = "OK", Position = new Position(0, 0) }.Draw(buffer);
        Assert.AreEqual(" OK", buffer.ContentAsString.Split(Environment.NewLine)[0].TrimEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Slider_ShowValue_DrawsNumberAfterTrack()
    {
        MockBuffer buffer = new(new Size(12, 1));
        new Slider(Theme()) { Minimum = 1, Maximum = 9, Value = 5, ShowValue = true, Position = new Position(0, 0), ViewSize = new Size(12, 1) }.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Split(Environment.NewLine)[0].TrimEnd().EndsWith(" 5"));
    }
}

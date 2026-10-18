using iConsole.Controls;
using iConsole.Controls.Charts;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class SliderAndLineChartTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    [TestMethod]
    public void Slider_ThumbColumn_And_ValueAt_RoundTrip()
    {
        Assert.AreEqual(0, Slider.ThumbColumn(0, 0, 100, 11));
        Assert.AreEqual(5, Slider.ThumbColumn(50, 0, 100, 11));
        Assert.AreEqual(10, Slider.ThumbColumn(100, 0, 100, 11));
        Assert.AreEqual(50, Slider.ValueAt(5, 0, 100, 11));
    }

    [TestMethod]
    public void Slider_Value_IsClampedAndRaisesEvent()
    {
        Slider slider = new(Theme()) { Minimum = 0, Maximum = 10 };
        int raised = 0;
        slider.ValueChanged += (_, _) => raised++;
        slider.Value = 99;
        Assert.AreEqual(10, slider.Value);
        slider.Value = 10;
        Assert.AreEqual(1, raised);
    }

    [TestMethod]
    public void Slider_Draw_PutsThumbOnTrack()
    {
        MockBuffer buffer = new(new Size(5, 1));
        new Slider(Theme()) { Minimum = 0, Maximum = 4, Value = 2, Position = new Position(0, 0), ViewSize = new Size(5, 1) }.Draw(buffer);
        Assert.AreEqual("━━●──", buffer.ContentAsString.Split(Environment.NewLine)[0]);
    }

    [TestMethod]
    public void LineChart_Render_RisingSeriesFillsDiagonal()
    {
        string[] rows = LineChart.Render([0, 1], 1, 1);
        Assert.AreEqual(1, rows.Length);
        Assert.AreNotEqual('⠀', rows[0][0]);
    }

    [TestMethod]
    public void LineChart_Render_EmptySeriesIsBlankBraille()
    {
        string[] rows = LineChart.Render([], 3, 2);
        CollectionAssert.AreEqual(new[] { "⠀⠀⠀", "⠀⠀⠀" }, rows);
    }
}

using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class StepIndicatorAndAlertTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StepIndicator_Render_MarksDoneCurrentAndPending()
    {
        Assert.AreEqual("●─◉─○─○", StepIndicator.Render(4, 1));
        Assert.AreEqual("◉", StepIndicator.Render(1, 0));
        Assert.AreEqual(string.Empty, StepIndicator.Render(0, 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StepIndicator_Width_FollowsCount()
    {
        StepIndicator indicator = new(Theme()) { Count = 3 };
        Assert.AreEqual(5, (int)indicator.ViewSize.Width);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Alert_Draw_ShowsGlyphAndMessage()
    {
        MockBuffer buffer = new(new Size(20, 1));
        new Alert(Theme()) { Kind = BadgeKind.Error, Text = "Disk full", Position = new Position(0, 0), ViewSize = new Size(20, 1) }.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.StartsWith("✖ Disk full"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Alert_Draw_ClearsWhenTextIsEmpty()
    {
        MockBuffer buffer = new(new Size(10, 1));
        Alert alert = new(Theme()) { Text = "hello", Position = new Position(0, 0), ViewSize = new Size(10, 1) };
        alert.Draw(buffer);
        alert.Text = string.Empty;
        alert.Draw(buffer);
        Assert.AreEqual(string.Empty, buffer.ContentAsString.Split(Environment.NewLine)[0].Trim());
    }
}

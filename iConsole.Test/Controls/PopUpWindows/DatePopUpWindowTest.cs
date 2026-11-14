using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Controls.PopUpWindows;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using iConsole.Data;

namespace iConsole.Controls.PopUpWindows.Test;

[TestClass]
public class DatePopUpWindowTest
{
    // A zone with a fixed offset, so the offset line and the marker can be asserted exactly.
    private static TimeZoneInfo FixedZone() =>
        TimeZoneInfo.CreateCustomTimeZone("Test +05:30", TimeSpan.FromHours(5.5), "Test +05:30", "Test +05:30");

    private static DatePopUpWindow CreateWindow()
    {
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());
        return new DatePopUpWindow(mockTheme.Object)
        {
            ViewSize = new Size(80, 22),
            Position = new Position(0, 0),
            LocalTimeZone = FixedZone,
        };
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ShouldWriteDateLines()
    {
        var win = CreateWindow();
        var buffer = new ScreenBuffer(80, 22);

        win.Draw(buffer);
        win.Draw(buffer);

        Assert.IsTrue(buffer.Contains("Date:"));
        Assert.IsTrue(buffer.Contains("Time:"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_WritesTheTimeZoneAndItsUtcOffset()
    {
        var win = CreateWindow();
        var buffer = new ScreenBuffer(80, 22);

        win.Draw(buffer);
        win.Draw(buffer);

        // Rows 0 to 5 of the interior hold the text; the interior starts one row and one column in.
        Assert.AreEqual("Time zone: Test +05:30", buffer.Text(1, 3, 78).TrimEnd());
        Assert.AreEqual("UTC offset: +05:30 (standard time)", buffer.Text(1, 4, 78).TrimEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_PutsTheMapInTheInteriorWithTheMarkerOnTheZonesMeridian()
    {
        var win = CreateWindow();
        var buffer = new ScreenBuffer(80, 22);

        win.Draw(buffer);
        win.Draw(buffer);

        // The map is 72 columns, centred in the 78 inner columns (3 on the left), and its first row is interior row 7.
        string[] expected = SchematicWorldMap.Render(72, 12, 82.5, 45);
        for (int row = 0; row < 12; row++)
        {
            Assert.AreEqual(expected[row], buffer.Text(1 + 3, 1 + 7 + row, 72), "map row " + row);
        }

        // The marker is in map row 3 at column 52 (82.5° east), so screen column 1 + 3 + 52 on screen row 1 + 7 + 3.
        Assert.AreEqual('@', buffer.CharAt(1 + 3 + 52, 1 + 7 + 3));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ClearsTheRowsBelowTheMap()
    {
        var win = CreateWindow();
        var buffer = new ScreenBuffer(80, 22);

        win.Draw(buffer);
        win.Draw(buffer);

        // The last interior row (row 19) is blank.
        Assert.AreEqual(string.Empty, buffer.Text(1, 20, 78).TrimEnd());
    }
}

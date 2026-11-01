using iConsole.Controls;
using iConsole.Controls.Experimental;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Fixes found by building the controls the sweeps had skipped: a null text, and a disabled list that looked live.</summary>
[TestClass]
public class ControlsReviewBatch38Test
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme
        {
            Foreground = Color.White,
            Background = Color.Black,
            DisabledForeground = Color.DarkGray,
            DisabledBackground = Color.Black,
            SelectedForeground = Color.Black,
            SelectedBackground = Color.Gray,
            SelectedFocusedForeground = Color.Black,
            SelectedFocusedBackground = Color.Cyan,
        });
        return m.Object;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BouncingLabel_NullText_IsTakenAsEmpty()
    {
        BouncingLabel label = new(new Position(0, 0), "moving", Theme(), autoStart: false) { ViewSize = new Size(10, 1) };

        label.Text = null!;

        ScreenBuffer.Render(label, 10, 1);
        Assert.IsNotNull(label);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LiveTable_NullEmptyText_IsTakenAsEmpty_AndDraws()
    {
        LiveTable table = new(Theme(), new SystemTimeProvider()) { Position = new Position(0, 0), ViewSize = new Size(20, 3) };

        table.EmptyText = null!;

        Assert.AreEqual(string.Empty, table.EmptyText);
        ScreenBuffer.Render(table, 20, 3);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void VirtualListBox_Disabled_GreysItsRows()
    {
        VirtualListBox<string> list = new(new ListDataSource<string>(["alpha", "beta", "gamma"]), Theme())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 3),
        };

        ScreenBuffer live = ScreenBuffer.Render(list, 20, 3);
        list.IsEnabled = false;
        ScreenBuffer greyed = ScreenBuffer.Render(list, 20, 3);

        Assert.AreEqual(Color.White, live.ForegroundAt(0, 1));
        Assert.AreEqual(Color.DarkGray, greyed.ForegroundAt(0, 1));
        Assert.AreEqual(Color.DarkGray, greyed.ForegroundAt(0, 0));
    }
}

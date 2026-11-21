using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Test.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Controls;

[TestClass]
public class WindowFocusHighlightTest
{
    /// <summary>A window that opts in to the focus highlight, as the file panels do.</summary>
    private sealed class HighlightedWindow(iConsole.Services.IThemeService themeService) : Window(themeService)
    {
        protected override bool HighlightFocusedBorder => true;
    }

    [TestMethod]
    public void FocusedFrameIsHighlightedAndTitleKeepsThePlainForeground()
    {
        var theme = new TestThemeService();
        var window = new HighlightedWindow(theme) { Title = "Left", ViewSize = new Size(20, 3), CanFocus = true };
        window.SetFocus();

        var screen = ScreenBuffer.Render(window, 20, 3);

        // " Left " is centred on the 20-column top line, starting at column 7.
        Assert.AreEqual(theme.CurrentColorScheme.FocusedBorder, screen.ForegroundAt(0, 0), "corner");
        Assert.AreEqual(theme.CurrentColorScheme.FocusedBorder, screen.ForegroundAt(0, 1), "side");
        Assert.AreEqual(theme.CurrentColorScheme.Foreground, screen.ForegroundAt(8, 0), "title");
    }

    [TestMethod]
    public void UnfocusedFrameIsNotHighlighted()
    {
        var theme = new TestThemeService();
        var window = new HighlightedWindow(theme) { Title = "Left", ViewSize = new Size(20, 3), CanFocus = true };

        var screen = ScreenBuffer.Render(window, 20, 3);

        Assert.AreNotEqual(theme.CurrentColorScheme.FocusedBorder, screen.ForegroundAt(0, 0));
    }
}

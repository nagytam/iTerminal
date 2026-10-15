using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Test.Controls.StatusBar;

[TestClass]
public class StatusBarItemTest
{
    private sealed class TestStatusBarItem(IThemeService themeService) : iConsole.Controls.StatusBar.StatusBarItem(themeService)
    {
    }

    [TestMethod]
    public void Draw_WritesPaddedTextToBuffer()
    {
        TestStatusBarItem item = new(new ThemeService())
        {
            Text = "Hi",
            ViewSize = new Size(5, 1),
            Position = new Position(0, 0),
        };
        MockBuffer buffer = new(new Size(5, 1));

        item.Draw(buffer);

        Assert.AreEqual("Hi   " + Environment.NewLine, buffer.ContentAsString);
    }
}

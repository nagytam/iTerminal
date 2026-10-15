using iConsole.Display;
using iConsole.Drivers;

namespace iConsole.Test.Drivers;

[TestClass]
[DoNotParallelize]
public class VirtualSequenceConsoleDriverTest
{
    [TestMethod]
    [Timeout(5000)]
    public void Title_SetSameValueTwice_DoesNotThrow()
    {
        var driver = new VirtualSequenceConsoleDriver();
        var title = driver.Title;

        driver.Title = title;
        driver.Title = title;

        Assert.AreEqual(title, driver.Title);
    }

    [TestMethod]
    [Timeout(5000)]
    public void IsCursorVisible_SetSameValueTwice_DoesNotThrow()
    {
        var driver = new VirtualSequenceConsoleDriver();

        driver.IsCursorVisible = false;
        driver.IsCursorVisible = false;

        Assert.AreEqual(false, driver.IsCursorVisible);
    }

    [TestMethod]
    [Timeout(5000)]
    public void ForegroundColor_SetTwice_PreservesValue()
    {
        var driver = new VirtualSequenceConsoleDriver();

        driver.ForegroundColor = Color.White;
        driver.ForegroundColor = Color.White;

        Assert.AreEqual(Color.White, driver.ForegroundColor);
    }

    [TestMethod]
    [Timeout(5000)]
    public void BackgroundColor_SetTwice_PreservesValue()
    {
        var driver = new VirtualSequenceConsoleDriver();

        driver.BackgroundColor = Color.Black;
        driver.BackgroundColor = Color.Black;

        Assert.AreEqual(Color.Black, driver.BackgroundColor);
    }

    [TestMethod]
    [Timeout(5000)]
    public void RefreshWindowSize_UpdatesWindowSizeToCurrentConsoleValues()
    {
        var driver = new VirtualSequenceConsoleDriver();

        driver.RefreshWindowSize();

        // The driver reads the size through ConsoleWindowSizeHelper, which prefers the visible
        // window over Console.WindowWidth/Height and falls back to a default size when the host
        // has no console at all (CI, VS Test) — never to 0×0, which would leave nothing to draw into.
        var expected = ConsoleWindowSizeHelper.GetVisibleWindowSize();
        Assert.AreEqual(expected.Width, driver.WindowSize.Width);
        Assert.AreEqual(expected.Height, driver.WindowSize.Height);
        Assert.IsTrue(driver.WindowSize.Width > 0 && driver.WindowSize.Height > 0);
    }

    [TestMethod]
    [Timeout(5000)]
    public void Normalize_LeavesViewportSizeUntouched()
    {
        var normalized = ConsoleWindowSizeHelper.Normalize(new iConsole.Data.Size(100, 9999));

        if (ConsoleWindowTestHelper.TryGetVisibleWindowSize(out var visibleWindowSize))
        {
            Assert.AreEqual(Math.Min(100, visibleWindowSize.Width), normalized.Width);
            Assert.AreEqual(Math.Min(9999, visibleWindowSize.Height), normalized.Height);
            return;
        }

        Assert.AreEqual(100, normalized.Width);
        Assert.AreEqual(9999, normalized.Height);
    }

    [TestMethod]
    [Timeout(5000)]
    public void GetVisiblePosition_ClampsBelowViewportOriginToZero()
    {
        var visiblePosition = ConsoleWindowSizeHelper.GetVisiblePosition(new iConsole.Data.Position(2, 3), new iConsole.Data.Position(10, 20));

        Assert.AreEqual(0, visiblePosition.X);
        Assert.AreEqual(0, visiblePosition.Y);
    }
}

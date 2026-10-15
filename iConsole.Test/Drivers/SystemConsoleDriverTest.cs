using iConsole.Display;
using iConsole.Drivers;

namespace iConsole.Test.Drivers;

[TestClass]
[DoNotParallelize]
public class SystemConsoleDriverTest
{
    [TestMethod]
    [Timeout(5000)]
    public void Normalize_LeavesViewportSizeUntouched()
    {
        var normalized = ConsoleWindowSizeHelper.Normalize(new iConsole.Data.Size(117, 9999));

        if (ConsoleWindowTestHelper.TryGetVisibleWindowSize(out var visibleWindowSize))
        {
            Assert.AreEqual(Math.Min(117, visibleWindowSize.Width), normalized.Width);
            Assert.AreEqual(Math.Min(9999, visibleWindowSize.Height), normalized.Height);
            return;
        }

        Assert.AreEqual(117, normalized.Width);
        Assert.AreEqual(9999, normalized.Height);
    }

    [TestMethod]
    [Timeout(5000)]
    public void Normalize_LeavesNormalWindowSizeUntouched()
    {
        var normalized = ConsoleWindowSizeHelper.Normalize(new iConsole.Data.Size(120, 30));

        if (ConsoleWindowTestHelper.TryGetVisibleWindowSize(out var visibleWindowSize))
        {
            Assert.AreEqual(Math.Min(120, visibleWindowSize.Width), normalized.Width);
            Assert.AreEqual(Math.Min(30, visibleWindowSize.Height), normalized.Height);
            return;
        }

        Assert.AreEqual(120, normalized.Width);
        Assert.AreEqual(30, normalized.Height);
    }

    [TestMethod]
    [Timeout(5000)]
    public void GetBufferPosition_AddsViewportOrigin()
    {
        var bufferPosition = ConsoleWindowSizeHelper.GetBufferPosition(new iConsole.Data.Position(5, 7), new iConsole.Data.Position(10, 20));

        Assert.AreEqual(15, bufferPosition.X);
        Assert.AreEqual(27, bufferPosition.Y);
    }

    [TestMethod]
    [Timeout(5000)]
    public void GetVisiblePosition_SubtractsViewportOrigin()
    {
        var visiblePosition = ConsoleWindowSizeHelper.GetVisiblePosition(new iConsole.Data.Position(15, 27), new iConsole.Data.Position(10, 20));

        Assert.AreEqual(5, visiblePosition.X);
        Assert.AreEqual(7, visiblePosition.Y);
    }

    [TestMethod]
    [Timeout(5000)]
    public void Title_SetSameValueTwice_DoesNotThrow()
    {
        var driver = new SystemConsoleDriver();
        var title = driver.Title;

        driver.Title = title;
        driver.Title = title;

        Assert.AreEqual(title, driver.Title);
    }

    [TestMethod]
    [Timeout(5000)]
    public void IsCursorVisible_SetSameValueTwice_DoesNotThrow()
    {
        var driver = new SystemConsoleDriver();

        driver.IsCursorVisible = false;
        driver.IsCursorVisible = false;

        Assert.AreEqual(false, driver.IsCursorVisible);
    }

    [TestMethod]
    [Timeout(5000)]
    public void ForegroundColor_SetTwice_PreservesValue()
    {
        var driver = new SystemConsoleDriver();

        driver.ForegroundColor = Color.White;
        driver.ForegroundColor = Color.White;

        Assert.AreEqual(Color.White, driver.ForegroundColor);
    }

    [TestMethod]
    [Timeout(5000)]
    public void BackgroundColor_SetTwice_PreservesValue()
    {
        var driver = new SystemConsoleDriver();

        driver.BackgroundColor = Color.Black;
        driver.BackgroundColor = Color.Black;

        Assert.AreEqual(Color.Black, driver.BackgroundColor);
    }
}

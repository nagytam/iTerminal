using iConsole.Buffers;
using iConsole.Data;
using iConsole.Desktops;
using iConsole.Drivers;
using iConsole.Screens;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Desktops;

[TestClass]
[DoNotParallelize]
public class DesktopAdditionalTest
{
    private sealed class ScreenStub(IThemeService themeService) : iConsole.Controls.Control(themeService), IScreen
    {
        public IDesktop Desktop { get; set; } = default!;
        public int WindowSizeChangedCount { get; private set; }
        public Size? LastSize { get; private set; }

        public override void OnWindowSizeChanged(object? sender, Size e)
        {
            WindowSizeChangedCount++;
            LastSize = e;
        }
    }

    private sealed class CursorControlStub(IThemeService themeService) : iConsole.Controls.Control(themeService)
    {
        public bool CursorVisible { get; set; }
        public override bool IsCursorVisible => CursorVisible;
    }

    [TestMethod]
    [Timeout(5000)]
    public void Constructor_SetsDesktopOnInitialScreenAndAddsItToScreens()
    {
        var consoleDriver = new Mock<IConsoleDriver>(MockBehavior.Strict);

        var themeService = new Mock<IThemeService>(MockBehavior.Strict);
        var screen = new ScreenStub(themeService.Object);
        var buffer = new MockBuffer(new Size(10, 10));

        var desktop = new Desktop(consoleDriver.Object, screen, buffer, Enumerable.Empty<iConsole.Jobs.IJob>());

        Assert.AreSame(desktop, screen.Desktop);
        Assert.AreEqual(1, desktop.Screens.Count);
        Assert.AreSame(screen, desktop.Screens.First());
    }

    [TestMethod]
    [Timeout(5000)]
    public void OnIdle_SetsBufferCursorVisibilityBasedOnChildControls()
    {
        var consoleDriver = new Mock<IConsoleDriver>(MockBehavior.Strict);

        var themeService = new Mock<IThemeService>(MockBehavior.Strict);
        var screen = new ScreenStub(themeService.Object);
        var buffer = new MockBuffer(new Size(10, 10));

        var desktop = new Desktop(consoleDriver.Object, screen, buffer, Enumerable.Empty<iConsole.Jobs.IJob>());

        var cursorControl = new CursorControlStub(themeService.Object) { CursorVisible = false };
        screen.Controls.Add(cursorControl);

        desktop.OnIdle();
        Assert.AreEqual(false, buffer.Cursor.IsCursorVisible);

        cursorControl.CursorVisible = true;
        desktop.OnIdle();
        Assert.AreEqual(true, buffer.Cursor.IsCursorVisible);
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task StartAsync_PropagatesInitialWindowSizeToScreen()
    {
        var consoleDriver = new Mock<IConsoleDriver>(MockBehavior.Strict);
        consoleDriver.Setup(d => d.RefreshWindowSize());
        consoleDriver.SetupGet(d => d.WindowSize).Returns(new Size(120, 40));
        consoleDriver.Setup(d => d.Clear());

        var themeService = new Mock<IThemeService>(MockBehavior.Strict);
        var screen = new ScreenStub(themeService.Object);
        var buffer = new MockBuffer(new Size(0, 0));

        var desktop = new Desktop(consoleDriver.Object, screen, buffer, Enumerable.Empty<iConsole.Jobs.IJob>());

        await desktop.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await desktop.StopAsync(CancellationToken.None);

        Assert.AreEqual(1, screen.WindowSizeChangedCount);
        Assert.AreEqual(new Size(120, 40), screen.LastSize);
        Assert.AreEqual(new Size(120, 40), buffer.Size);
    }
}

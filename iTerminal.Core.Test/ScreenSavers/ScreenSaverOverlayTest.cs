using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Services;
using iTerminal.Core.ScreenSavers;
using Moq;

namespace iTerminal.Core.Test.ScreenSavers;

[TestClass]
public class ScreenSaverOverlayTest
{
    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    /// <summary>A window of 80 by 24 with the overlay on it, as the terminal screen has it, and a clock the test moves.</summary>
    private sealed class Fixture
    {
        public Fixture(ConsoleOwnership? ownership = null)
        {
            Time = new VirtualTimeProvider();
            Store = new SettingsService();
            Activity = new UserActivityService(Time);
            var theme = CreateThemeService();
            Root = new Control(theme) { ViewSize = new Size(80, 24) };
            Overlay = new ScreenSaverOverlay(theme, Store, Activity, Time, ownership);
            Root.AddControl(Overlay);
        }

        public VirtualTimeProvider Time { get; }
        public SettingsService Store { get; }
        public UserActivityService Activity { get; }
        public Control Root { get; }
        public ScreenSaverOverlay Overlay { get; }

        /// <summary>Lets the time pass, then gives the overlay an idle pass, as the render loop does when the screen is quiet.</summary>
        public void Pass(TimeSpan elapsed)
        {
            Time.Advance(elapsed);
            Overlay.OnIdle();
        }

        public void Configure(ScreenSaverKind kind, int minutes)
        {
            Store.Set(Settings.ScreenSaverEffect, kind.ToString());
            Store.Set(Settings.ScreenSaverIdleMinutes, minutes.ToString());
        }
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Off_NeverStarts_however_long_the_terminal_is_left_alone()
    {
        var fixture = new Fixture();
        fixture.Configure(ScreenSaverKind.Off, minutes: 1);

        fixture.Pass(TimeSpan.FromHours(3));

        Assert.IsFalse(fixture.Overlay.IsActive);
        Assert.IsFalse(fixture.Overlay.IsModal);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void StartsOnlyOnceTheChosenIdleTimeHasPassed()
    {
        var fixture = new Fixture();
        fixture.Configure(ScreenSaverKind.Starfield, minutes: 2);

        fixture.Pass(TimeSpan.FromMinutes(1));
        Assert.IsFalse(fixture.Overlay.IsActive, "Started before the idle time.");

        fixture.Pass(TimeSpan.FromMinutes(1));
        Assert.IsTrue(fixture.Overlay.IsActive, "Did not start at the idle time.");
        Assert.IsTrue(fixture.Overlay.IsModal, "A running screen saver must take the keys.");
        Assert.IsTrue(fixture.Overlay.IsVisible);
    }

    [TestMethod]
    [Timeout(60_000)]
    [DataRow(ScreenSaverKind.Starfield)]
    [DataRow(ScreenSaverKind.MatrixRain)]
    [DataRow(ScreenSaverKind.Clock)]
    public void EachKind_Starts(ScreenSaverKind kind)
    {
        var fixture = new Fixture();
        fixture.Configure(kind, minutes: 1);

        fixture.Pass(TimeSpan.FromMinutes(2));

        Assert.IsTrue(fixture.Overlay.IsActive);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AKeyPress_WakesTheScreen_AndIsUsedUpDoingSo()
    {
        var fixture = new Fixture();
        fixture.Configure(ScreenSaverKind.Clock, minutes: 1);
        fixture.Pass(TimeSpan.FromMinutes(2));

        var handled = await fixture.Overlay.HandleKeyEventAsync(new KeyEvent('a', ConsoleKey.A, false, false, false));

        Assert.IsTrue(handled);
        Assert.IsFalse(fixture.Overlay.IsActive);
        Assert.IsFalse(fixture.Overlay.IsModal, "A woken screen saver must give the keys back.");
        Assert.IsFalse(fixture.Overlay.IsVisible);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AKeyPress_WhenNotRunning_IsNotTaken()
    {
        var fixture = new Fixture();

        var handled = await fixture.Overlay.HandleKeyEventAsync(new KeyEvent('a', ConsoleKey.A, false, false, false));

        Assert.IsFalse(handled);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task APointerThatOnlyHovers_DoesNotWakeIt()
    {
        var fixture = new Fixture();
        fixture.Configure(ScreenSaverKind.Starfield, minutes: 1);
        fixture.Pass(TimeSpan.FromMinutes(2));

        var handled = await fixture.Overlay.HandleMouseEventAsync(new MouseEvent { IsHover = true });

        Assert.IsTrue(handled, "The hover must not reach the screen beneath.");
        Assert.IsTrue(fixture.Overlay.IsActive);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AClick_WakesIt()
    {
        var fixture = new Fixture();
        fixture.Configure(ScreenSaverKind.Starfield, minutes: 1);
        fixture.Pass(TimeSpan.FromMinutes(2));

        await fixture.Overlay.HandleMouseEventAsync(new MouseEvent { LeftClicked = true });

        Assert.IsFalse(fixture.Overlay.IsActive);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AKeyPress_RestartsTheIdleClock()
    {
        var fixture = new Fixture();
        fixture.Configure(ScreenSaverKind.Starfield, minutes: 1);
        fixture.Pass(TimeSpan.FromMinutes(2));

        // The input consumer records the key as activity before the overlay sees it.
        fixture.Activity.NoteActivity();
        await fixture.Overlay.HandleKeyEventAsync(new KeyEvent('a', ConsoleKey.A, false, false, false));
        fixture.Pass(TimeSpan.FromSeconds(30));

        Assert.IsFalse(fixture.Overlay.IsActive, "Started again before a full idle period had passed.");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task DoesNotStart_WhileTheTerminalIsLentToAShell()
    {
        var ownership = new ConsoleOwnership();
        var fixture = new Fixture(ownership);
        fixture.Configure(ScreenSaverKind.Starfield, minutes: 1);

        bool startedWhileLent = await ownership.LendAsync(() =>
        {
            fixture.Pass(TimeSpan.FromMinutes(5));
            return fixture.Overlay.IsActive;
        });

        Assert.IsFalse(startedWhileLent, "The screen saver covered a shell's screen.");
        fixture.Pass(TimeSpan.FromSeconds(1));
        Assert.IsTrue(fixture.Overlay.IsActive, "Did not start once the terminal was given back.");
    }

    [TestMethod]
    [Timeout(60_000)]
    public void WhileRunning_DrawsTheWholeWindow()
    {
        var fixture = new Fixture();
        fixture.Configure(ScreenSaverKind.MatrixRain, minutes: 1);
        fixture.Pass(TimeSpan.FromMinutes(2));
        var buffer = new Mock<IBuffer>();

        fixture.Overlay.Draw(buffer.Object);

        buffer.Verify(b => b.Write(It.IsAny<int>(), It.IsAny<Position>(), It.Is<Size>(s => s.Height == 1 && s.Width == 80), It.IsAny<ConxelString>()), Times.Exactly(24));
    }

    [TestMethod]
    [Timeout(60_000)]
    public void WhenNotRunning_DrawsNothing()
    {
        var fixture = new Fixture();
        var buffer = new Mock<IBuffer>();

        fixture.Overlay.Draw(buffer.Object);

        buffer.Verify(b => b.Write(It.IsAny<int>(), It.IsAny<Position>(), It.IsAny<Size>(), It.IsAny<ConxelString>()), Times.Never);
    }
}

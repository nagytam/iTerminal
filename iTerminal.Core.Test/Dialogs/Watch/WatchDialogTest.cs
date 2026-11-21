using iConsole.Services;
using iTerminal.Core.Dialogs.Watch;
using iTerminal.Core.Model.Watch;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Watch;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Watch;

[TestClass]
public class WatchDialogTest
{
    private static readonly DateTime Start = new(2026, 10, 14, 10, 0, 0);

    private sealed class FakeClock : IDateTimeProviderService
    {
        public DateTime Now { get; set; } = Start;

        public DateTime UtcNow => DateTime.SpecifyKind(Now, DateTimeKind.Utc);
    }

    private sealed class Fixture
    {
        public required WatchDialog Dialog { get; init; }
        public required FakeClock Clock { get; init; }
        public required WatchStopwatch Stopwatch { get; init; }
        public required WatchTimers Timers { get; init; }
    }

    private static Fixture Create()
    {
        var clock = new FakeClock();
        var settings = new Mock<ISettingsService>();
        var store = new WatchStore(settings.Object);
        var timers = new WatchTimers();
        var stopwatch = new WatchStopwatch();
        return new Fixture
        {
            Dialog = new WatchDialog(TestTheme(), clock, store, timers, stopwatch),
            Clock = clock,
            Stopwatch = stopwatch,
            Timers = timers,
        };
    }

    private static async Task TypeAsync(WatchDialog dialog, string text)
    {
        foreach (var c in text)
        {
            await dialog.HandleKeyEventAsync(Char(c));
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpensOnTheWorldClockWithTheComputersZoneFirst()
    {
        var fixture = Create();

        await ShowAsync(fixture.Dialog);

        AssertScreenContains(Render(fixture.Dialog), "This computer");
        AssertScreenContains(Render(fixture.Dialog), "World clock");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheWorldClockSwitchesBetweenDigitalAndAnalogFaces()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        var digital = Render(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Char('v'));
        var analog = Render(fixture.Dialog);

        Assert.IsTrue(analog.Contains("•"), "the analog face has a centre");
        Assert.IsFalse(digital.Contains("•"), "the digital face has none");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnAlarmTypedOnTheAlarmPageIsListedWithItsNextRing()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await fixture.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(fixture.Dialog, "07:30 Wake up");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        var screen = Render(fixture.Dialog);
        AssertScreenContains(screen, "07:30");
        AssertScreenContains(screen, "Wake up");
        AssertScreenContains(screen, "Next alarm: 07:30, in 21:30:00");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task LeftAndRightKeepToTheTypedTextInsteadOfSwitchingTabs()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await fixture.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(fixture.Dialog, "07");

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));

        AssertScreenContains(Render(fixture.Dialog), "New alarm (HH:mm label): 07_");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscCancelsAnOpenEntryAndLeavesTheDialogOpen()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await fixture.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(fixture.Dialog, "x");

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsTrue(fixture.Dialog.IsVisible, "the first Esc only cancels the entry");
        AssertScreenDoesNotContain(Render(fixture.Dialog), "New alarm (HH:mm label)");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheStopwatchStartsAndTakesALapOnTheKeys()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));

        await fixture.Dialog.HandleKeyEventAsync(Char(' '));
        fixture.Clock.Now = Start.AddSeconds(2);
        await fixture.Dialog.HandleKeyEventAsync(Char('l'));
        fixture.Clock.Now = Start.AddSeconds(3);

        Assert.IsTrue(fixture.Stopwatch.IsRunning);
        Assert.AreEqual(1, fixture.Stopwatch.Laps.Count);
        AssertScreenContains(Render(fixture.Dialog), "Running");

        // The time is drawn in large digits, so the lap (plain text) is what can be read off the screen.
        AssertScreenContains(Render(fixture.Dialog), "00:02.00");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATimerTypedOnTheTimersPageStartsAtOnce()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        for (int i = 0; i < 3; i++)
        {
            await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        }

        await fixture.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(fixture.Dialog, "5 Tea");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        var screen = Render(fixture.Dialog);
        AssertScreenContains(screen, "Running");
        AssertScreenContains(screen, "05:00");
        AssertScreenContains(screen, "Tea");
        Assert.AreEqual(WatchTimerState.Running, fixture.Timers.Rows(Start).Single().State);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AWorldClockZoneAddedByNameIsListed()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        await fixture.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(fixture.Dialog, "UTC");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        AssertScreenContains(Render(fixture.Dialog), "UTC");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnknownZoneIsRefusedWithTheReasonOnTheMessageLine()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        await fixture.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(fixture.Dialog, "Mars/Base");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        AssertScreenContains(Render(fixture.Dialog), "is not a time zone");
    }
}

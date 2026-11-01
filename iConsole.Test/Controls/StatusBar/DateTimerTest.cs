using iConsole.Controls.StatusBar;
using iConsole.Display;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Controls.StatusBar;

/// <summary>The clock in the status bar advances on its own timer once started, and stops when disposed.</summary>
[TestClass]
public class DateTimerTest
{
    private sealed class Clock : IDateTimeProviderService
    {
        private long _ticks = new DateTime(2026, 1, 2, 3, 4, 5).Ticks;

        public DateTime Now => new(Interlocked.Read(ref _ticks));

        public DateTime UtcNow => Now.ToUniversalTime();

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }

    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static async Task<bool> BecomesAsync(Func<bool> condition, int timeoutMs = 8000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        return condition();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnceStarted_TheTextFollowsTheClockWithoutAnyIdlePass()
    {
        Clock clock = new();
        using Date date = new(Theme(), clock);
        Assert.AreEqual("2026-01-02 03:04:05", date.Text);

        date.Start();
        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.IsTrue(await BecomesAsync(() => date.Text == "2026-01-02 03:04:35"), date.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BeforeItIsStarted_TheTimerDoesNotRun()
    {
        Clock clock = new();
        using Date date = new(Theme(), clock);
        clock.Advance(TimeSpan.FromSeconds(30));
        await Task.Delay(1500);
        Assert.AreEqual("2026-01-02 03:04:05", date.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AfterItIsDisposed_TheTextStopsChanging()
    {
        Clock clock = new();
        Date date = new(Theme(), clock);
        date.Start();
        date.Dispose();
        clock.Advance(TimeSpan.FromSeconds(30));
        await Task.Delay(1500);
        Assert.AreEqual("2026-01-02 03:04:05", date.Text);
        date.Start();
        date.Dispose();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnIdle_UpdatesTheTextAndSaysWhenNothingChanged()
    {
        Clock clock = new();
        using Date date = new(Theme(), clock);
        clock.Advance(TimeSpan.FromSeconds(2));
        date.OnIdle();
        Assert.AreEqual("2026-01-02 03:04:07", date.Text);
        date.OnIdle();
        Assert.AreEqual("2026-01-02 03:04:07", date.Text);
    }
}

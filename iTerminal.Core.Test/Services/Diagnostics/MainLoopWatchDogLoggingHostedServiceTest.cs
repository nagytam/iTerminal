using iConsole.Diagnostics;
using iConsole.Services;
using iTerminal.Core.Services.Diagnostics;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace iTerminal.Core.Test.Services.Diagnostics;

/// <summary>The watchdog's stalls and recoveries reach the log as a warning and an information line, and stop reaching it when the service stops.</summary>
[TestClass]
public class MainLoopWatchDogLoggingHostedServiceTest
{
    private sealed class ManualTime : ITimeProvider
    {
        private long _milliseconds;
        public DateTime Now => DateTime.UnixEpoch.AddMilliseconds(_milliseconds);
        public DateTime UtcNow => Now;
        public long MonotonicMilliseconds => _milliseconds;
        public void Advance(long milliseconds) => _milliseconds += milliseconds;
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static (MainLoopWatchDog WatchDog, ManualTime Time) Watchdog()
    {
        ManualTime time = new();
        return (new MainLoopWatchDog(time, TimeSpan.FromMilliseconds(200)), time);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APassPastItsBudgetIsLoggedAsAWarning()
    {
        var (watchDog, time) = Watchdog();
        CollectingSink sink = new();
        ILogger logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        MainLoopWatchDogLoggingHostedService service = new(watchDog, logger);
        await service.StartAsync(CancellationToken.None);

        using (watchDog.Watch("render"))
        {
            time.Advance(250);
            watchDog.Check();
        }

        LogEvent warning = sink.Events.Single(e => e.Level == LogEventLevel.Warning);
        Assert.AreEqual(LogEventLevel.Warning, warning.Level);
        StringAssert.Contains(warning.RenderMessage(), "Main loop stalled: \"render\"");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ThePassEndingIsLoggedAsInformation()
    {
        var (watchDog, time) = Watchdog();
        CollectingSink sink = new();
        MainLoopWatchDogLoggingHostedService service = new(watchDog, new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger());
        await service.StartAsync(CancellationToken.None);

        using (watchDog.Watch("key"))
        {
            time.Advance(250);
            watchDog.Check();
        }

        LogEvent recovered = sink.Events.Last();
        Assert.AreEqual(LogEventLevel.Information, recovered.Level);
        StringAssert.Contains(recovered.RenderMessage(), "Main loop recovered: \"key\"");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APassWithinItsBudgetLogsNothing()
    {
        var (watchDog, time) = Watchdog();
        CollectingSink sink = new();
        MainLoopWatchDogLoggingHostedService service = new(watchDog, new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger());
        await service.StartAsync(CancellationToken.None);

        using (watchDog.Watch("key"))
        {
            time.Advance(50);
            watchDog.Check();
        }

        Assert.AreEqual(0, sink.Events.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AfterStoppingNothingIsLogged()
    {
        var (watchDog, time) = Watchdog();
        CollectingSink sink = new();
        MainLoopWatchDogLoggingHostedService service = new(watchDog, new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger());
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        using (watchDog.Watch("render"))
        {
            time.Advance(250);
            watchDog.Check();
        }

        Assert.AreEqual(0, sink.Events.Count, "a stopped service must no longer listen to the watchdog");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithNoLoggerAStallIsDroppedWithoutFailingTheWatchdog()
    {
        var (watchDog, time) = Watchdog();
        MainLoopWatchDogLoggingHostedService service = new(watchDog, logger: null);
        await service.StartAsync(CancellationToken.None);

        using (watchDog.Watch("render"))
        {
            time.Advance(250);
            watchDog.Check();
        }

        Assert.AreEqual(1, watchDog.StallCount);
    }
}

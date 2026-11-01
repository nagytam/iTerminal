using iConsole.Data;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs.Benchmark;
using iTerminal.Core.Services.Benchmark;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class PerformanceBenchmarkDialogTest
{
    private sealed class FixedProbe(string id, string name, BenchmarkCategory category, BenchmarkMeasurement measurement) : IBenchmarkProbe
    {
        public string Id => id;

        public string Name => name;

        public BenchmarkCategory Category => category;

        public Task<BenchmarkMeasurement> MeasureAsync(CancellationToken cancellationToken) => Task.FromResult(measurement);
    }

    /// <summary>A probe that never finishes on its own: it waits until the run is cancelled.</summary>
    private sealed class StuckProbe : IBenchmarkProbe
    {
        public string Id => "stuck";

        public string Name => "Stuck probe";

        public BenchmarkCategory Category => BenchmarkCategory.Memory;

        public async Task<BenchmarkMeasurement> MeasureAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new BenchmarkMeasurement(null, string.Empty, 1, true);
        }
    }

    private static PerformanceBenchmarkDialog NewDialog(params IBenchmarkProbe[] probes) =>
        new(new PerformanceBenchmarkService(probes), TestTheme());

    private static IBenchmarkProbe[] FastProbes() =>
    [
        new FixedProbe("cpu.single", "Single thread", BenchmarkCategory.Cpu, new BenchmarkMeasurement(1000, "Mops/s", 500, true)),
        new FixedProbe("network.latency", "Internet latency", BenchmarkCategory.Network, BenchmarkMeasurement.Unavailable("ms", 30, false, "could not reach speed.cloudflare.com")),
    ];

    [Timeout(60_000)]
    [TestMethod]
    public async Task BeforeARunItListsEveryProbeAsWaitingAndSaysHowToStart()
    {
        var dialog = NewDialog(FastProbes());
        await ShowAsync(dialog);

        var screen = Render(dialog);
        AssertScreenContains(screen, "Performance benchmark");
        AssertScreenContains(screen, "Enter starts the benchmark.");
        AssertScreenContains(screen, "Single thread");
        AssertScreenContains(screen, "Internet latency");
        AssertScreenContains(screen, "waiting");
        AssertScreenContains(screen, "Enter:run  Esc:close");
        Assert.IsFalse(dialog.IsRunning);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterRunsTheBenchmarkAndShowsTheScoresAndTheOverall()
    {
        var dialog = NewDialog(FastProbes());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.RunTask;

        var screen = Render(dialog);
        AssertScreenContains(screen, "Finished. Enter runs it again.");
        AssertScreenContains(screen, "Overall score");
        AssertScreenContains(screen, "2,000");
        AssertScreenContains(screen, "1,000 Mops/s");
        AssertScreenContains(screen, "Single thread");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AProbeThatCouldNotMeasureSaysWhyAndIsLeftOutOfTheOverall()
    {
        var dialog = NewDialog(FastProbes());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.RunTask;

        var screen = Render(dialog);
        AssertScreenContains(screen, "unavailable: could not reach speed.cloudflare.com");
        // Only the one scored probe counts: the overall is its category, the CPU's 2,000.
        AssertScreenContains(screen, "Overall score");
        Assert.AreEqual(2000, dialog.CurrentReport.Overall!.Value, 1e-6);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscDuringARunCancelsItAndKeepsTheDialogOpen()
    {
        var dialog = NewDialog([.. FastProbes().Take(1), new StuckProbe()]);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await WaitUntilAsync(() => Render(dialog).ToString().Contains("Measuring Stuck probe"), "the stuck probe to start");
        AssertScreenContains(Render(dialog), "Esc:cancel the run");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));
        await dialog.RunTask;

        var screen = Render(dialog);
        AssertScreenContains(screen, "Cancelled; the scores so far are shown.");
        AssertScreenContains(screen, "Single thread");
        Assert.IsFalse(dialog.IsRunning);
        Assert.IsTrue(dialog.IsVisible, "the first Esc only stops the run");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheDialogIsBoundToCtrlShiftB()
    {
        var command = new PerformanceBenchmarkDialogCommand(NewDialog());

        Assert.AreEqual("Performance benchmark", command.Title);
        Assert.AreEqual(new Key(ConsoleKey.B, true, false, true), command.Key);
    }
}

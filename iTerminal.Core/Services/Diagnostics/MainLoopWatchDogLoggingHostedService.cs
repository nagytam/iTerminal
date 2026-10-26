using iConsole.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace iTerminal.Core.Services.Diagnostics;

/// <summary>
/// Writes the main-loop watchdog's reports to the log: a warning when a key handler or render pass runs past its budget,
/// and an information line when that pass finally ends. The watchdog itself has no logger, so the application supplies one.
/// </summary>
public sealed class MainLoopWatchDogLoggingHostedService(MainLoopWatchDog watchDog, Serilog.ILogger? logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        watchDog.Stalled += OnStalled;
        watchDog.Recovered += OnRecovered;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        watchDog.Stalled -= OnStalled;
        watchDog.Recovered -= OnRecovered;
        return Task.CompletedTask;
    }

    // A missing logger drops the line rather than failing the watchdog's thread.
    private void OnStalled(object? sender, StallEventArgs e) =>
        logger?.Warning("Main loop stalled: {Label} has run for {ElapsedMilliseconds} ms", e.Label, e.ElapsedMilliseconds);

    private void OnRecovered(object? sender, StallEventArgs e) =>
        logger?.Information("Main loop recovered: {Label} took {ElapsedMilliseconds} ms in total", e.Label, e.ElapsedMilliseconds);
}

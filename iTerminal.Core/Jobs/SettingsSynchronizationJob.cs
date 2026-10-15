using iConsole.Jobs;
using iTerminal.Core.Services;

namespace iTerminal.Core.Jobs;

public class SettingsSynchronizationJob(ISettingsService settingsService) 
    : Job
{
    private ISettingsService _settingsService { get; set; } = settingsService;

    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            WriteIfDirty();
            if (!cancellationToken.IsCancellationRequested)
            {
                // WaitHandle.WaitOne returns immediately once cancellationToken fires, unlike
                // Thread.Sleep, so shutdown (e.g. F10 Quit) isn't stalled waiting out the interval.
                cancellationToken.WaitHandle.WaitOne(1000);
            }
        }
        return Task.CompletedTask;
    }

    private void WriteIfDirty()
    {
        try
        {
            if (_settingsService.IsDirty)
            {
                _settingsService.Write();
            }
        }
        catch
        {
        }
    }

    public override void OnAfterStop()
    {
        WriteIfDirty();
    }
}

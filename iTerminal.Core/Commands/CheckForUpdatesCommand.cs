using iConsole.Commands;
using iTerminal.Core.Jobs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Help > Check for Updates: asks <see cref="UpdateCheckJob"/> to look for a newer release now. The key handler only
/// wakes the job; the check itself runs on the job's thread, so a slow network never holds up the keyboard.
/// </summary>
public class CheckForUpdatesCommand(UpdateCheckJob updateCheckJob) : Command
{
    public override string Title => "Check for updates";
    public override string Description => "Look for a newer iTerminal release now and offer to install it.";

    public override Task ExecuteAsync(object? args)
    {
        updateCheckJob.CheckNow();
        return Task.CompletedTask;
    }
}

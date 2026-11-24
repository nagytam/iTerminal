using iConsole.Commands;
using iConsole.Localization;
using iConsole.Prompts;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Tasks;

namespace iTerminal.Core.Commands;

/// <summary>
/// F10 Quit (and its Ctrl+C alternate, and File &gt; Quit) for the application. Quitting cancels the desktop, which
/// stops every background task at once, so a copy or move still running would be cut off part-way. When such a task
/// is still queued or running, this asks first, and the default answer is no.
/// </summary>
/// <remarks>
/// The question is not awaited on the key handler: the keyboard loop runs that handler to completion before it reads
/// the next key, and the prompt can only be answered through that same loop. The answer is awaited in the background
/// instead, and the quit follows it.
/// </remarks>
public sealed class QuitWithConfirmationCommand(TaskManager? taskManager, IPromptService? promptService) : QuitCommand
{
    // A second F10 while the question is up is the same question, not a second one.
    private int _asking;

    public override Task ExecuteAsync(object? args)
    {
        int running = CountRunningTasks();
        if (running == 0 || promptService is null)
        {
            return base.ExecuteAsync(args);
        }

        if (Interlocked.Exchange(ref _asking, 1) == 0)
        {
            _ = AskThenQuitAsync(running, args);
        }

        return Task.CompletedTask;
    }

    private async Task AskThenQuitAsync(int running, object? arg)
    {
        try
        {
            bool? confirmed = await promptService!.ConfirmAsync(new ConfirmationPrompt(
                string.Format(
                    UiText.Get("Quit.ConfirmTasks", "{0} background task(s) are still running. Quit anyway?"),
                    running))
            {
                DefaultValue = false,
            });
            if (confirmed == true)
            {
                await base.ExecuteAsync(arg);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _asking, 0);
        }
    }

    /// <summary>
    /// Everything the task manager still holds except the panel and item-information refreshes, which the application starts
    /// itself as the cursor or folder changes and which lose nothing when they stop.
    /// </summary>
    internal int CountRunningTasks()
    {
        if (taskManager is null)
        {
            return 0;
        }

        return taskManager.Tasks.AsSnapshot().Count(task => task is not (FilePanelRefreshTask or FileInformationRefreshTask));
    }
}

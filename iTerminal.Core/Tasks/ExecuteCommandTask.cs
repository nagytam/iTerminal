using iConsole.Tasks;
using System.Diagnostics;
using iTerminal.Core.Model;
using iConsole.Interfaces;

namespace iTerminal.Core.Tasks;

public class ExecuteCommandTask : BackgroundTask
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IContentModel? ContentModel { get; set; }
    public TerminalModel? TerminalModel { get; set; }
    public Task? Task { get; private set; }

    /// <summary>
    /// Completes when the command has finished and been added to the history — or at once when
    /// there was nothing to run. Unlike <see cref="Task"/>, it exists from construction, so a caller
    /// can hold on to it before the <see cref="TaskManager"/> has even picked the task up.
    /// </summary>
    public Task Completion => _completion.Task;

    public override Task ExecuteAsync()
    {
        if (TerminalModel is not null && ContentModel is not null)
        {
            Task = Task.Run(async () =>
            {
                try
                {
                    await ContentModel.RunTerminalCommandAsync(TerminalModel);
                }
                catch (Exception exception)
                {
                    TerminalModel.Command.Error += exception.Message;
                }

                try
                {
                    TerminalModel.AddCommandHistory(TerminalModel.Command);
                }
                finally
                {
                    _completion.TrySetResult();
                }
            });
        }
        else
        {
            _completion.TrySetResult();
        }

        return Task.CompletedTask;
    }
}

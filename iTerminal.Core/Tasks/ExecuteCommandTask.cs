using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Tasks;
using iTerminal.Core.Model;
using iTerminal.Core.Services.Shell;

namespace iTerminal.Core.Tasks;

public class ExecuteCommandTask : BackgroundTask
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IContentModel? ContentModel { get; set; }
    public TerminalModel? TerminalModel { get; set; }
    public Task? Task { get; private set; }

    /// <summary>
    /// An app built-in such as <c>shell</c>: runs in place of a command, on the same background task, and
    /// records its result on the command like one.
    /// </summary>
    public Func<Task>? Builtin { get; set; }

    /// <summary>
    /// Completes when the command has finished and been added to the history — or at once when
    /// there was nothing to run. Unlike <see cref="Task"/>, it exists from construction, so a caller
    /// can hold on to it before the <see cref="TaskManager"/> has even picked the task up.
    /// </summary>
    public Task Completion => _completion.Task;

    public override Task ExecuteAsync()
    {
        if (TerminalModel is not null && (Builtin is not null || ContentModel is not null))
        {
            Task = Task.Run(async () =>
            {
                try
                {
                    if (Builtin is not null)
                    {
                        await Builtin();
                    }
                    else
                    {
                        await RunCommandAsync();
                    }
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

    /// <summary>
    /// A local folder with a shell session available runs in the session, so variables and the folder carry
    /// over; every other command (a remote or archive drive, or no session here) runs as it always has.
    /// </summary>
    private async Task RunCommandAsync()
    {
        var terminal = TerminalModel!;
        var sessions = terminal.ShellSessions;
        if (sessions is { IsAvailable: true } && ContentModel!.Url is { ContainerType: ContainerType.FileSystem })
        {
            await RunInSessionAsync(terminal, ContentModel, sessions);
            return;
        }

        await ContentModel!.RunTerminalCommandAsync(terminal);
    }

    private async Task RunInSessionAsync(TerminalModel terminal, IContentModel model, ILocalShellSessionService sessions)
    {
        var command = terminal.Command;
        var startUrl = model.Url;
        var folder = string.IsNullOrEmpty(command.WorkingDirectoryUrl.Path) ? startUrl.Path : command.WorkingDirectoryUrl.Path;

        command.StartTime = DateTime.Now;
        command.Host = Environment.MachineName;
        command.Username = Environment.UserName;
        command.EnvironmentVariables = CommandEnvironment.Describe();

        var result = await sessions.RunAsync(folder, command.Command, CancellationToken);

        command.Output = string.IsNullOrEmpty(result.Output) ? null : result.Output;
        var error = result.Error;
        var stateNote = result.State switch
        {
            ShellRunState.Interrupted => UiText.Get("Terminal.ShellInterrupted", "Interrupted with Esc: the shell session was ended, so its variables and folder are gone."),
            ShellRunState.Ended => UiText.Get("Terminal.ShellEnded", "The shell ended (exit, or an error that stopped it). The next command starts a new session."),
            _ => null,
        };
        if (stateNote is not null)
        {
            error = string.IsNullOrEmpty(error) ? stateNote : error + Environment.NewLine + stateNote;
        }

        command.Error = string.IsNullOrEmpty(error) ? null : error;
        command.ExitCode = result.ExitCode;
        command.ExitTime = DateTime.Now;

        if (result.State == ShellRunState.Completed && !string.IsNullOrEmpty(result.Folder))
        {
            terminal.FollowPanel(model, startUrl, result.Folder);
        }
    }
}

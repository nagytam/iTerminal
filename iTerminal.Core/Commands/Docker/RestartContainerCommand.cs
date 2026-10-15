using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services.Docker;

namespace iTerminal.Core.Commands.Docker;

/// <summary>Ctrl+Shift+F9: restarts the container under the cursor (starts it, if it was stopped).</summary>
public sealed class RestartContainerCommand(IContentModel contentModel, IContentModelService contentModelService)
    : DockerContainerCommand(contentModel, contentModelService)
{
    public override string Title => "Restart container";
    public override string Description => "Restart the Docker container under the cursor.";

    public override Key? Key { get; init; } = new Key(ConsoleKey.F9, true, false, true);

    public override Task ExecuteAsync(object? args)
    {
        if (Target is { } target)
        {
            target.Model.RunContainerAction(target.Container, DockerContainerAction.Restart);
        }

        return Task.CompletedTask;
    }
}

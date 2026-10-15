using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services.Docker;

namespace iTerminal.Core.Commands.Docker;

/// <summary>Shift+F9: starts the container under the cursor, or stops it when it is running.</summary>
public sealed class StartStopContainerCommand(IContentModel contentModel, IContentModelService contentModelService)
    : DockerContainerCommand(contentModel, contentModelService)
{
    public override string Title => "Start / stop container";
    public override string Description => "Start the Docker container under the cursor, or stop it if it is running.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } = new Key(ConsoleKey.F9, true, false, false);

    public override Task ExecuteAsync(object? args)
    {
        if (Target is { } target)
        {
            target.Model.RunContainerAction(
                target.Container,
                target.Container.IsRunning ? DockerContainerAction.Stop : DockerContainerAction.Start);
        }

        return Task.CompletedTask;
    }
}

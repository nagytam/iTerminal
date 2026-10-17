using iConsole.Commands;

namespace iTerminal.Core.Dialogs.Video;

/// <summary>A command made of a title and an action, for the transport buttons of the video viewer.</summary>
internal sealed class VideoActionCommand(Func<string> title, string description, Action action) : Command
{
    public override string Title => title();

    public override string Description => description;

    public override Task ExecuteAsync(object? args)
    {
        action();
        return Task.CompletedTask;
    }
}

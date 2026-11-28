using iConsole.Commands;

namespace iTerminal.DevelopmentStudio.Dialogs;

/// <summary>The command of a settings page's button: it runs the page's action when the button is pressed.</summary>
internal sealed class StudioActionCommand(string title, Action action) : Command
{
    public override string Title => title;

    public override Task ExecuteAsync(object? args)
    {
        action();
        return Task.CompletedTask;
    }
}

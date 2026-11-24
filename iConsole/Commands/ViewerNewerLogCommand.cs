using iConsole.Data;
using iConsole.Dialogs;

namespace iConsole.Commands;

/// <summary>Ctrl+PageDown in the F3 viewer: shows the log that was shown after this one. A user can rebind it in keymap.json.</summary>
public class ViewerNewerLogCommand(FileViewerDialog viewer) : Command
{
    public override string Title => "Newer log";
    public override string Description => "Show the log shown after this one in the viewer's log history.";

    public override Key? Key { get; init; } = new Key(ConsoleKey.PageDown, false, false, true);

    public override bool CanExecute() => viewer.NewerLog is not null;

    public override async Task ExecuteAsync(object? args)
    {
        if (viewer.NewerLog is { } url)
        {
            await viewer.ShowLogAsync(url);
        }
    }
}

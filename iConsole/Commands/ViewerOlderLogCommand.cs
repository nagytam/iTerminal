using iConsole.Data;
using iConsole.Dialogs;

namespace iConsole.Commands;

/// <summary>Ctrl+PageUp in the F3 viewer: shows the log that was shown before this one. A user can rebind it in keymap.json.</summary>
public class ViewerOlderLogCommand(FileViewerDialog viewer) : Command
{
    public override string Title => "Older log";
    public override string Description => "Show the log shown before this one in the viewer's log history.";

    public override Key? Key { get; init; } = new Key(ConsoleKey.PageUp, false, false, true);

    public override bool CanExecute() => viewer.OlderLog is not null;

    public override async Task ExecuteAsync(object? arg)
    {
        if (viewer.OlderLog is { } url)
        {
            await viewer.ShowLogAsync(url);
        }
    }
}

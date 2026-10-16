using iConsole.Commands;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class ScreenManagerDialogCommand(IThemeService themeService) : Command
{
    public override string Title => "Screen manager";
    public override string Description => "Show and manage open screens.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F12, false, false, false);

    public override async Task ExecuteAsync(object? arg)
    {
        var dialog = new ScreenManagerDialog(themeService);
        if (Control is not null)
        {
            await Control.ShowDialogAsync(dialog);
        }
    }
}

using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Services;

namespace iConsole.Commands;

/// <summary>F5 in the hex dump: asks for an offset and goes to it.</summary>
public class ViewerGoToOffsetCommand(
    HexViewer hexViewer,
    IBeepService beepService,
    IDateTimeProviderService dateTimeProviderService,
    IThemeService themeService) : Command
{
    public override string Title => "F5-GoToOffset";
    public override Key? Key { get; init; } = new Key(ConsoleKey.F5, false, false, false);

    /// <summary>Only in the hex dump; in the text view F5 is go to line.</summary>
    public override bool CanExecute() => base.CanExecute() && hexViewer.IsVisible;

    public override async Task ExecuteAsync(object? arg)
    {
        var dialog = new GoToOffsetDialog(hexViewer, beepService, dateTimeProviderService, themeService);
        if (Control is not null)
        {
            await Control.ShowDialogAsync(dialog, Control.Parent?.Parent);
        }
    }
}

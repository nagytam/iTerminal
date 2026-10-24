using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Services;

namespace iConsole.Commands;

/// <summary>F5 in the viewer: asks for a line number and goes to it.</summary>
public class ViewerGoToLineCommand(
    TextViewerControl viewer,
    IBeepService beepService,
    IDateTimeProviderService dateTimeProviderService,
    IThemeService themeService) : Command
{
    public override string Title => "F5-GoToLine";
    public override Key? Key { get; init; } = new Key(ConsoleKey.F5, false, false, false);

    /// <summary>Only in the text view: a hex dump has lines of bytes, not lines of text.</summary>
    public override bool CanExecute() => base.CanExecute() && viewer.IsVisible;

    public override async Task ExecuteAsync(object? arg)
    {
        var dialog = new GoToLineDialog(viewer, beepService, dateTimeProviderService, themeService);
        if (Control is not null)
        {
            await Control.ShowDialogAsync(dialog, Control.Parent?.Parent);
        }
    }
}

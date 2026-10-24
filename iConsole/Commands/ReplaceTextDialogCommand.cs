using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Services;

namespace iConsole.Commands;

/// <summary>Ctrl+R: opens the <see cref="ReplaceTextDialog"/> over the editor.</summary>
public class ReplaceTextDialogCommand(
    TextEditorControl editor,
    IBeepService beepService,
    IDateTimeProviderService dateTimeProviderService,
    IThemeService themeService)
    : Command
{
    public override string Title => "^R-Replace";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.R, false, false, true);

    public override bool CanExecute() => !editor.IsReadOnly;

    public override async Task ExecuteAsync(object? arg)
    {
        var dialog = new ReplaceTextDialog(editor, beepService, dateTimeProviderService, themeService);
        if (Control is not null)
        {
            await Control.ShowDialogAsync(dialog, Control.Parent?.Parent);
        }
    }
}

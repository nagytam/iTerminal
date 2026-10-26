using iConsole.Data;
using iConsole.Localization;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>Ctrl+Shift+F3: choose the language of the interface (<see cref="LanguageDialog"/>).</summary>
public class LanguageDialogCommand(LanguageDialog languageDialog) : ShowDialogCommand<LanguageDialog>(languageDialog)
{
    // The title of a menu entry is read once, so it is the dialog's own resource key that names it; the menu
    // re-reads it with the rest of the entries when the language changes.
    public override string Title => "Language…";
    public override string Description => "Choose the language of the menus, hints and dialogs: English, Deutsch, Français, Español, Italiano or Magyar.";

    // Ctrl+Shift+F3. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F3, true, false, true);

    protected override void OnBeforeShow(LanguageDialog dialog) => dialog.Load();
}

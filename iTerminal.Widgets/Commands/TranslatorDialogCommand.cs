using iTerminal.Core.Commands;
using iConsole.Data;
using iTerminal.Widgets.Dialogs.Translator;

namespace iTerminal.Widgets.Commands;

public class TranslatorDialogCommand(TranslatorDialog translatorDialog) : ShowDialogCommand<TranslatorDialog>(translatorDialog)
{
    public override string Title => "Translator";
    public override string Description => "Open the translator: detect a language and translate text through a translation API or the connected AI model.";

    // Ctrl+Shift+F4. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F4, true, false, true);
}

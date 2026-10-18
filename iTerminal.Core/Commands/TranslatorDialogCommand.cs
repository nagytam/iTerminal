using iConsole.Data;
using iTerminal.Core.Dialogs.Translator;

namespace iTerminal.Core.Commands;

public class TranslatorDialogCommand(TranslatorDialog translatorDialog) : ShowDialogCommand<TranslatorDialog>(translatorDialog)
{
    public override string Title => "Translator";
    public override string Description => "Open the translator: detect a language and translate text through a translation API or the connected AI model.";

    // Ctrl+Shift+F4. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F4, true, false, true);
}

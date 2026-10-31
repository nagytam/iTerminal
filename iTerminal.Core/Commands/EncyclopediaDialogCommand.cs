using iConsole.Data;
using iTerminal.Core.Dialogs.Encyclopedia;

namespace iTerminal.Core.Commands;

public class EncyclopediaDialogCommand(EncyclopediaDialog encyclopediaDialog) : ShowDialogCommand<EncyclopediaDialog>(encyclopediaDialog)
{
    public override string Title => "Encyclopedia";
    public override string Description => "Open the encyclopedia: look a term up in Wikipedia, Wiktionary and more, and read the article.";

    // Ctrl+Shift+W. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.W, true, false, true);
}

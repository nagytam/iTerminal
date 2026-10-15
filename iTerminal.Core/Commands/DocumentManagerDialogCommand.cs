using iConsole.Data;
using iTerminal.Core.Dialogs.Documents;

namespace iTerminal.Core.Commands;

/// <summary>
/// Opens the document manager.
///
/// <para>Alt+F3 rather than a letter chord, for the reason set out in the hot-key notes: the focused
/// file panel claims every Alt+printable combination for its type-to-search filter, so a global
/// binding has to be a function key. F3 is the "view" key, which is what this is a search across.</para>
/// </summary>
public class DocumentManagerDialogCommand(DocumentManagerDialog documentManagerDialog)
    : ShowDialogCommand<DocumentManagerDialog>(documentManagerDialog)
{
    public override string Title => "Documents";

    public override string Description => "Search the indexed documents folder and preview what is in it.";

    // Alt+F3. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F3, false, true, false);
}

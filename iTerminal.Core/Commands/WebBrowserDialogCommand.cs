using iConsole.Data;
using iTerminal.Core.Dialogs.Web;

namespace iTerminal.Core.Commands;

public class WebBrowserDialogCommand(WebBrowserDialog webBrowserDialog) : ShowDialogCommand<WebBrowserDialog>(webBrowserDialog)
{
    public override string Title => "Web Browser";
    public override string Description => "Open the Web Browser: a full-screen, text-mode browser with tabs, bookmarks, history, find and forms.";

    // Ctrl+S. Not Ctrl+T: that opens the Tools menu (its hot key is T). Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.S, false, false, true);
}

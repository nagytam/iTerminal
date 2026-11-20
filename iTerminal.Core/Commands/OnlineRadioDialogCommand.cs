using iConsole.Data;
using iTerminal.Core.Dialogs.Radio;

namespace iTerminal.Core.Commands;

public class OnlineRadioDialogCommand(OnlineRadioDialog onlineRadioDialog) : ShowDialogCommand<OnlineRadioDialog>(onlineRadioDialog)
{
    public override string Title => "Online radio";
    public override string Description => "Listen to internet radio stations: play, pause and stop, mark favourites and filter the list.";

    // Ctrl+Shift+Y. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Y, true, false, true);
}

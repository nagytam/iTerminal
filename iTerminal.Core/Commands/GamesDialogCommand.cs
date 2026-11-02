using iConsole.Data;
using iTerminal.Core.Dialogs.Games;

namespace iTerminal.Core.Commands;

public class GamesDialogCommand(GamesDialog gamesDialog) : ShowDialogCommand<GamesDialog>(gamesDialog)
{
    public override string Title => "Games";
    public override string Description => "Play Tic-Tac-Toe, Chess, Go, Nine Men's Morris or Snake, against the computer or a friend.";

    // No hot key: every Ctrl+Shift+F-key is taken. Open it from Tools > Games or the command palette.
    public override Key? Key { get; init; } = null;
}

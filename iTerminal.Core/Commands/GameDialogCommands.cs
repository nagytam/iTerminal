using iConsole.Data;
using iTerminal.Core.Dialogs.Games;

namespace iTerminal.Core.Commands;

public class TicTacToeDialogCommand(TicTacToeDialog dialog) : ShowDialogCommand<TicTacToeDialog>(dialog)
{
    public override string Title => "Tic-Tac-Toe";
    public override string Description => "Play Tic-Tac-Toe against the computer or a friend.";

    // No hot key: every Ctrl+Shift+F-key is taken. Open it from Tools or the command palette.
    public override Key? Key { get; init; } = null;
}

public class ChessDialogCommand(ChessDialog dialog) : ShowDialogCommand<ChessDialog>(dialog)
{
    public override string Title => "Chess";
    public override string Description => "Play Chess against the computer or a friend.";

    // No hot key: every Ctrl+Shift+F-key is taken. Open it from Tools or the command palette.
    public override Key? Key { get; init; } = null;
}

public class GoDialogCommand(GoDialog dialog) : ShowDialogCommand<GoDialog>(dialog)
{
    public override string Title => "Go";
    public override string Description => "Play Go (9x9, 13x13 or 19x19) against the computer or a friend.";

    // No hot key: every Ctrl+Shift+F-key is taken. Open it from Tools or the command palette.
    public override Key? Key { get; init; } = null;
}

public class NineMensMorrisDialogCommand(NineMensMorrisDialog dialog) : ShowDialogCommand<NineMensMorrisDialog>(dialog)
{
    public override string Title => "Nine Men's Morris";
    public override string Description => "Play Nine Men's Morris against the computer or a friend.";

    // No hot key: every Ctrl+Shift+F-key is taken. Open it from Tools or the command palette.
    public override Key? Key { get; init; } = null;
}

public class SnakeDialogCommand(SnakeDialog dialog) : ShowDialogCommand<SnakeDialog>(dialog)
{
    public override string Title => "Snake";
    public override string Description => "Play Snake: steer the snake to the food without hitting a wall or yourself.";

    // No hot key: every Ctrl+Shift+F-key is taken. Open it from Tools or the command palette.
    public override Key? Key { get; init; } = null;
}

public class TetrisDialogCommand(TetrisDialog dialog) : ShowDialogCommand<TetrisDialog>(dialog)
{
    public override string Title => "Tetris";
    public override string Description => "Play Tetris: stack the falling pieces into full rows.";

    // No hot key: every Ctrl+Shift+F-key is taken. Open it from Tools or the command palette.
    public override Key? Key { get; init; } = null;
}

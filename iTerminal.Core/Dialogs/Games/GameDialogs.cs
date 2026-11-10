using iConsole.Services;
using iTerminal.Core.Services.Games;

namespace iTerminal.Core.Dialogs.Games;

/// <summary>Tic-Tac-Toe, in a dialog of its own. The position is kept while the app runs, so closing the dialog does not end the game.</summary>
public class TicTacToeDialog(IThemeService themeService, ITimeProvider timeProvider)
    : GamesDialog(nameof(TicTacToeDialog), "Tic-Tac-Toe", [new TicTacToeGame()], themeService, timeProvider);

/// <summary>Chess, in a dialog of its own. The position is kept while the app runs, so closing the dialog does not end the game.</summary>
public class ChessDialog(IThemeService themeService, ITimeProvider timeProvider)
    : GamesDialog(nameof(ChessDialog), "Chess", [new ChessGame()], themeService, timeProvider);

/// <summary>Go, in a dialog of its own. The position is kept while the app runs, so closing the dialog does not end the game.</summary>
public class GoDialog(IThemeService themeService, ITimeProvider timeProvider)
    : GamesDialog(nameof(GoDialog), "Go", [new GoGame()], themeService, timeProvider);

/// <summary>Nine Men's Morris, in a dialog of its own. The position is kept while the app runs, so closing the dialog does not end the game.</summary>
public class NineMensMorrisDialog(IThemeService themeService, ITimeProvider timeProvider)
    : GamesDialog(nameof(NineMensMorrisDialog), "Nine Men's Morris", [new NineMensMorrisGame()], themeService, timeProvider);

/// <summary>Snake, in a dialog of its own. The position is kept while the app runs, so closing the dialog does not end the game.</summary>
public class SnakeDialog(IThemeService themeService, ITimeProvider timeProvider)
    : GamesDialog(nameof(SnakeDialog), "Snake", [new SnakeGame()], themeService, timeProvider);

/// <summary>Tetris, in a dialog of its own. The position is kept while the app runs, so closing the dialog does not end the game.</summary>
public class TetrisDialog(IThemeService themeService, ITimeProvider timeProvider)
    : GamesDialog(nameof(TetrisDialog), "Tetris", [new TetrisGame()], themeService, timeProvider);

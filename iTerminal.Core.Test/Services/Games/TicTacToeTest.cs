using iTerminal.Core.Services.Games;

namespace iTerminal.Core.Test.Services.Games;

[TestClass]
public class TicTacToeTest
{
    [TestMethod]
    public void ThreeInARowWins()
    {
        TicTacToeGame game = new() { VsComputer = false };
        foreach (int square in new[] { 0, 3, 1, 4, 2 })
        {
            Assert.IsTrue(game.Place(square));
        }

        Assert.IsTrue(game.IsOver);
        Assert.AreEqual("X wins!", game.Status);
        Assert.IsFalse(game.Place(8), "no moves after the game is won");
    }

    [TestMethod]
    public void AFullBoardWithoutALineIsADraw()
    {
        TicTacToeGame game = new() { VsComputer = false };
        foreach (int square in new[] { 0, 1, 2, 4, 3, 5, 7, 6, 8 })
        {
            game.Place(square);
        }

        Assert.IsTrue(game.IsOver);
        Assert.AreEqual("A draw.", game.Status);
    }

    [TestMethod]
    public void ATakenSquareIsRefused()
    {
        TicTacToeGame game = new() { VsComputer = false };
        game.MoveCursor(0, 0);
        game.Activate();

        game.Activate();

        Assert.AreEqual("That square is taken.", game.Status);
    }

    [TestMethod]
    public void TheCursorStaysOnTheBoard()
    {
        TicTacToeGame game = new() { VsComputer = false };

        for (int i = 0; i < 5; i++)
        {
            game.MoveCursor(-1, -1);
        }

        game.Activate();
        Assert.AreEqual('X', game.CellAt(0));
    }

    [TestMethod]
    public void TheComputerBlocksALineAndTakesAWin()
    {
        char[] board = "XX  O    ".ToCharArray();
        Assert.AreEqual(2, TicTacToeGame.BestMove(board, 'O', new Random(1)), "block X");

        char[] win = "OO XX    ".ToCharArray();
        Assert.AreEqual(2, TicTacToeGame.BestMove(win, 'O', new Random(1)), "complete the row");
    }

    [TestMethod]
    public void TheComputerNeverLosesToAnyPlay()
    {
        // Every possible sequence of human moves, the computer answering each time.
        void Explore(TicTacToeGame game, int depth)
        {
            Assert.IsFalse(game.IsOver && game.Status == "X wins!", "the human won");
            if (game.IsOver)
            {
                return;
            }

            for (int square = 0; square < 9; square++)
            {
                if (game.CellAt(square) != ' ')
                {
                    continue;
                }

                TicTacToeGame next = Copy(game);
                next.Place(square);
                if (!next.IsOver)
                {
                    next.PlanComputerMove()!(CancellationToken.None)!();
                }

                Explore(next, depth + 1);
            }
        }

        TicTacToeGame start = new(new Random(5));
        Explore(start, 0);
    }

    private static TicTacToeGame Copy(TicTacToeGame game)
    {
        TicTacToeGame copy = new(new Random(5));
        // Replay the position by placing the same marks in a legal order.
        List<int> xs = [.. Enumerable.Range(0, 9).Where(i => game.CellAt(i) == 'X')];
        List<int> os = [.. Enumerable.Range(0, 9).Where(i => game.CellAt(i) == 'O')];
        for (int i = 0; i < xs.Count; i++)
        {
            copy.Place(xs[i]);
            if (i < os.Count)
            {
                copy.Place(os[i]);
            }
        }

        return copy;
    }

    [TestMethod]
    public void UndoTakesBackTheComputersReplyToo()
    {
        TicTacToeGame game = new(new Random(2));
        game.Activate();
        game.PlanComputerMove()!(CancellationToken.None)!();
        Assert.IsTrue(game.XToMove);

        game.Undo();

        Assert.IsFalse(game.CanUndo);
        Assert.IsTrue(Enumerable.Range(0, 9).All(i => game.CellAt(i) == ' '));
    }

    [TestMethod]
    public void ThePersonCannotPlayTheComputersMarks()
    {
        TicTacToeGame game = new(new Random(2));
        game.Activate();

        game.MoveCursor(1, 0);
        game.Activate();

        Assert.AreEqual(1, Enumerable.Range(0, 9).Count(i => game.CellAt(i) != ' '));
    }

    [TestMethod]
    public void TheBoardDrawsFiveRowsWithTheCursorOnTheCentre()
    {
        TicTacToeGame game = new();

        IReadOnlyList<GameCell[]> rows = game.Render();

        Assert.AreEqual(5, rows.Count);
        Assert.IsTrue(rows[2][2].Cursor);
    }
}

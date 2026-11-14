using iTerminal.Core.Services.Games;

namespace iTerminal.Core.Test.Services.Games;

[TestClass]
public class TetrisGameTest
{
    private const int I = 0;
    private const int O = 1;

    private static TetrisGame Falling(int piece = O)
    {
        TetrisGame game = new(new Random(1)) { VsComputer = false };
        game.SpawnForTest(piece);
        return game;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ItWaitsForAKeyAndDoesNotFallUntilStarted()
    {
        TetrisGame game = new(new Random(1)) { VsComputer = false };

        Assert.IsFalse(game.Tick(TimeSpan.FromSeconds(5)));
        game.MoveCursor(1, 0);

        Assert.IsTrue(game.IsFalling);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThePieceFallsOneRowPerIntervalAndTheRemainderCarriesOver()
    {
        TetrisGame game = Falling();
        int top = game.PieceTop;

        game.Tick(TimeSpan.FromMilliseconds(500));
        Assert.AreEqual(top, game.PieceTop);
        game.Tick(TimeSpan.FromMilliseconds(300));

        Assert.AreEqual(top + 1, game.PieceTop);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheArrowsSlideThePieceButNotThroughTheWalls()
    {
        TetrisGame game = Falling();
        for (int i = 0; i < 20; i++)
        {
            game.MoveCursor(-1, 0);
        }

        Assert.AreEqual(0, game.PieceCells.Min(c => c.X));
        for (int i = 0; i < 20; i++)
        {
            game.MoveCursor(1, 0);
        }

        Assert.AreEqual(TetrisGame.Columns - 1, game.PieceCells.Max(c => c.X));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TurningAPieceNextToTheWallKicksItBackIn()
    {
        TetrisGame game = Falling(I);
        for (int i = 0; i < 20; i++)
        {
            game.MoveCursor(1, 0);
        }

        Assert.IsTrue(game.TryChar('x'));
        Assert.IsTrue(game.PieceCells.All(c => c.X is >= 0 and < TetrisGame.Columns));
        Assert.AreEqual(1, game.PieceCells.Select(c => c.X).Distinct().Count(), "the bar stands upright");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AHardDropLocksThePieceOnTheFloorAndScores()
    {
        TetrisGame game = Falling();

        Assert.IsTrue(game.TryChar('d'));

        Assert.IsTrue(game.IsFilled(4, TetrisGame.Rows - 1));
        Assert.IsTrue(game.Score > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFullRowIsClearedAndScored()
    {
        TetrisGame game = Falling();
        for (int x = 0; x < TetrisGame.Columns; x++)
        {
            if (x is not (4 or 5))
            {
                game.FillForTest(x, TetrisGame.Rows - 1);
                game.FillForTest(x, TetrisGame.Rows - 2);
            }
        }

        game.TryChar('d');

        Assert.AreEqual(2, game.Lines);
        for (int x = 0; x < TetrisGame.Columns; x++)
        {
            Assert.IsFalse(game.IsFilled(x, TetrisGame.Rows - 1));
        }

        Assert.IsTrue(game.Score >= 300, "two rows score 300 at level 1, plus the drop points");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheGameEndsWhenANewPieceHasNoRoom()
    {
        TetrisGame game = new(new Random(1)) { VsComputer = false };
        for (int x = 0; x < TetrisGame.Columns; x++)
        {
            game.FillForTest(x, 0);
            game.FillForTest(x, 1);
        }

        game.SpawnForTest(O);

        Assert.IsTrue(game.IsOver);
        StringAssert.Contains(game.Status, "Game over");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PauseStopsTheFallAndNewStartsAgain()
    {
        TetrisGame game = Falling();
        game.Activate();
        int top = game.PieceTop;

        Assert.IsTrue(game.IsPaused);
        Assert.IsFalse(game.Tick(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(top, game.PieceTop);

        game.NewGame();
        Assert.AreEqual(0, game.Score);
        Assert.IsFalse(game.IsFalling);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheBagGivesEachOfTheSevenPiecesOnceBeforeRepeating()
    {
        TetrisGame game = new(new Random(3)) { VsComputer = false };
        List<int> pieces = [game.CurrentPiece, game.NextPiece];
        for (int i = 0; i < 5; i++)
        {
            game.TryChar('d');
            pieces.Add(game.NextPiece);
        }

        Assert.AreEqual(7, pieces.Distinct().Count());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ItRendersTheWholeBoardWithABorder()
    {
        TetrisGame game = Falling();

        IReadOnlyList<GameCell[]> rows = game.Render();

        Assert.AreEqual(TetrisGame.Rows / 2 + 1, rows.Count, "the rows in pairs, and the floor");
        Assert.IsTrue(rows.All(r => r.Length == TetrisGame.Columns + 2));
        // The O piece is two cells by two, which the half-block board shows as two coloured cells in one text row.
        Assert.AreEqual(2, rows.SelectMany(r => r).Count(c => c.Background is not null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheComputersBoardPlaysOnItsOwnClockAndDropsItsPiecesToTheFloor()
    {
        TetrisGame game = new(new Random(5));
        game.Activate();

        for (int i = 0; i < 400; i++)
        {
            game.Tick(TimeSpan.FromMilliseconds(100));
        }

        TetrisGame rival = game.Rival!;
        Assert.IsFalse(game.IsOver, "a planned board does not top out in forty seconds");
        Assert.IsTrue(Enumerable.Range(0, TetrisGame.Columns).Any(x => rival.IsFilled(x, TetrisGame.Rows - 1)), "the first piece lands on the floor");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheComputersStackToppingOutFirstLetsThePersonWin()
    {
        TetrisGame game = new(new Random(5));
        TetrisGame rival = game.Rival!;
        // Every column but the last is full, so the rival's next piece has no room at the top; the person's board stays open.
        for (int y = 0; y < TetrisGame.Rows; y++)
        {
            for (int x = 0; x < TetrisGame.Columns - 1; x++)
            {
                rival.FillForTest(x, y);
            }
        }

        game.Activate();
        for (int i = 0; i < 300 && !game.IsOver; i++)
        {
            game.Tick(TimeSpan.FromMilliseconds(100));
        }

        Assert.IsTrue(game.IsOver);
        StringAssert.StartsWith(game.Status, "You win");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheComputersBoardWaitsUntilThePersonStarts()
    {
        TetrisGame game = new(new Random(5));

        for (int i = 0; i < 50; i++)
        {
            game.Tick(TimeSpan.FromMilliseconds(100));
        }

        Assert.IsFalse(Enumerable.Range(0, TetrisGame.Columns).Any(x => game.Rival!.IsFilled(x, TetrisGame.Rows - 1)));
        Assert.AreEqual(0, game.Rival!.Score);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheVersusBoardsAreDrawnSideBySide()
    {
        TetrisGame game = new(new Random(2));

        IReadOnlyList<GameCell[]> rows = game.Render();

        Assert.AreEqual(TetrisGame.Rows / 2 + 2, rows.Count, "a label row, the boards, and the floor");
        Assert.AreEqual(3, rows[0].Length, "the labels: You, a gap, Computer");
        Assert.IsTrue(rows.Skip(1).All(r => r.Length == 2 * (TetrisGame.Columns + 2) + 1), "two boards and a gap between them");
    }
}

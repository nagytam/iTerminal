using iTerminal.Core.Services.Games;

namespace iTerminal.Core.Test.Services.Games;

[TestClass]
public class TetrisGameTest
{
    private const int I = 0;
    private const int O = 1;

    private static TetrisGame Falling(int piece = O)
    {
        TetrisGame game = new(new Random(1));
        game.SpawnForTest(piece);
        return game;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ItWaitsForAKeyAndDoesNotFallUntilStarted()
    {
        TetrisGame game = new(new Random(1));

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
        TetrisGame game = new(new Random(1));
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
        TetrisGame game = new(new Random(3));
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

        Assert.AreEqual(TetrisGame.Rows, rows.Count);
        Assert.IsTrue(rows.All(r => r.Length == TetrisGame.Columns + 2));
        Assert.AreEqual(4, rows.SelectMany(r => r).Count(c => c.Background is not null));
    }
}

using iConsole.Display;
using iTerminal.Core.Services.Games;

namespace iTerminal.Core.Test.Services.Games;

[TestClass]
public class SnakeGameTest
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(120);

    private static SnakeGame Create() => new(new Random(1));

    [Timeout(60_000)]
    [TestMethod]
    public void TheSnakeStartsStoppedAndNothingMovesItBeforeTheFirstKey()
    {
        SnakeGame game = Create();
        (int X, int Y) start = game.Head;

        Assert.IsFalse(game.Tick(TimeSpan.FromSeconds(10)));

        Assert.AreEqual(start, game.Head);
        Assert.AreEqual(3, game.Body.Count);
        Assert.IsFalse(game.IsOver);
        Assert.AreEqual("Press an arrow key to start.", game.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheFirstArrowKeyStartsItAndItMovesOncePer120Milliseconds()
    {
        SnakeGame game = Create();
        game.MoveCursor(1, 0);

        Assert.IsFalse(game.Tick(TimeSpan.FromMilliseconds(119)));
        Assert.AreEqual(5, game.Head.X);

        Assert.IsTrue(game.Tick(TimeSpan.FromMilliseconds(1)));
        Assert.AreEqual(6, game.Head.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AReversedFirstKeyIsIgnoredAndDoesNotStartTheSnake()
    {
        SnakeGame game = Create();

        game.MoveCursor(-1, 0);

        Assert.IsFalse(game.Tick(TimeSpan.FromSeconds(1)));
        Assert.AreEqual((5, 6), game.Head);
        Assert.AreEqual("Press an arrow key to start.", game.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheRemainderOfATickCarriesIntoTheNext()
    {
        SnakeGame game = Create();
        game.Activate();

        Assert.IsFalse(game.Tick(TimeSpan.FromMilliseconds(100)));
        Assert.IsTrue(game.Tick(TimeSpan.FromMilliseconds(100)), "200 ms is one step");
        Assert.AreEqual(6, game.Head.X);
        Assert.IsTrue(game.Tick(TimeSpan.FromMilliseconds(40)), "the carried 80 ms and these 40 make the next step");
        Assert.AreEqual(7, game.Head.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AReversalOntoTheBodyIsIgnored()
    {
        SnakeGame game = Create();
        game.Activate();

        game.MoveCursor(-1, 0);
        game.Tick(Step);

        Assert.AreEqual((6, 6), game.Head);
        Assert.IsFalse(game.IsOver);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TwoTurnsWithinOneStepCannotFoldTheSnakeOntoItsNeck()
    {
        SnakeGame game = Create();
        game.Activate();

        // Up, then left: left is a reversal of the heading the snake is still on, so it is refused.
        game.MoveCursor(0, -1);
        game.MoveCursor(-1, 0);
        game.Tick(Step);

        Assert.AreEqual((5, 5), game.Head);
        Assert.IsFalse(game.IsOver);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EatingGrowsTheSnakeAndRaisesTheScore()
    {
        SnakeGame game = Create();
        game.Activate();
        game.PutFoodForTest((6, 6));

        game.Tick(Step);

        Assert.AreEqual(4, game.Body.Count);
        Assert.AreEqual("Score 1.", game.Status);
        Assert.IsFalse(game.Body.Contains(game.Food), "the new food is never under the snake");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HittingTheWallEndsTheGame()
    {
        SnakeGame game = Create();
        game.Activate();

        // The head starts at x = 5 on a 20-wide board, so the fifteenth step leaves it.
        game.Tick(TimeSpan.FromMilliseconds(15 * 120));

        Assert.IsTrue(game.IsOver);
        StringAssert.StartsWith(game.Status, "Game over with 0 points.");
        Assert.IsFalse(game.Tick(TimeSpan.FromSeconds(5)), "nothing moves once the game is over");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HittingItsOwnBodyEndsTheGame()
    {
        SnakeGame game = Create();
        game.Activate();

        // Each step eats, so the snake grows and its body keeps every cell it has passed: down, left, then up into the neck.
        game.MoveCursor(0, 1);
        game.PutFoodForTest((5, 7));
        game.Tick(Step);
        game.MoveCursor(-1, 0);
        game.PutFoodForTest((4, 7));
        game.Tick(Step);
        Assert.IsFalse(game.IsOver);

        game.MoveCursor(0, -1);
        game.PutFoodForTest((4, 6));
        game.Tick(Step);

        Assert.IsTrue(game.IsOver);
        Assert.AreEqual(5, game.Body.Count, "a crash does not change the length");
        Assert.AreEqual("Game over with 2 points. N to play again.", game.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EnterStartsPausesAndResumes()
    {
        SnakeGame game = Create();

        game.Activate();
        Assert.AreEqual("Score 0.", game.Status);

        game.Activate();
        Assert.AreEqual("Paused. Enter to go on.", game.Status);
        Assert.IsFalse(game.Tick(TimeSpan.FromMilliseconds(500)));
        Assert.AreEqual(5, game.Head.X);

        game.Activate();
        Assert.IsTrue(game.Tick(Step));
        Assert.AreEqual(6, game.Head.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PauseStopsMovementWithoutMovingTheSnake()
    {
        SnakeGame game = Create();
        game.Activate();
        game.Tick(Step);
        (int X, int Y) before = game.Head;

        game.Pause();

        Assert.AreEqual(before, game.Head);
        Assert.IsFalse(game.Tick(TimeSpan.FromSeconds(3)));
        Assert.AreEqual(before, game.Head);
        Assert.AreEqual("Paused. Enter to go on.", game.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PauseDoesNothingToAStoppedOrFinishedSnake()
    {
        SnakeGame game = Create();

        game.Pause();

        Assert.AreEqual("Press an arrow key to start.", game.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NewGameStartsAgainWithThreeCellsAndNotOver()
    {
        SnakeGame game = Create();
        game.Activate();
        game.Tick(TimeSpan.FromSeconds(10));
        Assert.IsTrue(game.IsOver);

        game.Undo();
        game.NewGame();

        Assert.IsFalse(game.IsOver);
        Assert.AreEqual(3, game.Body.Count);
        Assert.AreEqual((5, 6), game.Head);
        Assert.AreEqual("Press an arrow key to start.", game.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FoodIsNeverPlacedOnTheBody()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            SnakeGame game = new(new Random(seed));
            Random steering = new(seed);
            game.Activate();
            for (int step = 0; step < 300 && !game.IsOver; step++)
            {
                (int dx, int dy)[] directions = [(1, 0), (-1, 0), (0, 1), (0, -1)];
                (int dx, int dy) = directions[steering.Next(directions.Length)];
                game.MoveCursor(dx, dy);
                game.Tick(Step);

                Assert.IsFalse(game.Body.Contains(game.Food), $"seed {seed}, step {step}: food under the snake");
                if (!game.IsOver)
                {
                    Assert.IsTrue(game.Head.X is >= 0 and < 20 && game.Head.Y is >= 0 and < 12, "the head stays on the board");
                }
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheSnakeHasNoUndoNoComputerAndDrawsItsHeadAndFood()
    {
        SnakeGame game = Create();

        Assert.IsFalse(game.OffersUndo);
        Assert.IsFalse(game.OffersComputerOpponent);
        Assert.IsFalse(game.CanUndo);
        Assert.IsFalse(game.IsComputerTurn);
        Assert.IsNull(game.PlanComputerMove());

        IReadOnlyList<GameCell[]> rows = game.Render();
        Assert.AreEqual(12, rows.Count);
        Assert.IsTrue(rows.All(r => r.Length == 20));
        Assert.AreEqual(Color.Yellow, rows[6][5].Background, "the head");
        Assert.AreEqual(Color.Green, rows[6][4].Background, "the body");
        Assert.AreEqual(Color.Red, rows[game.Food.Y][game.Food.X].Background, "the food");
    }
}

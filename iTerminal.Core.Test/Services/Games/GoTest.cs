using iTerminal.Core.Services.Games;

namespace iTerminal.Core.Test.Services.Games;

[TestClass]
public class GoTest
{
    private static int At(int x, int y, int size = 9) => y * size + x;

    private static GoGame TwoPlayers() => new(new Random(1)) { VsComputer = false };

    private static void PlayAll(GoGame game, params (int X, int Y)[] moves)
    {
        foreach ((int x, int y) in moves)
        {
            Assert.IsTrue(game.TryPlay(At(x, y)), $"({x},{y}) was refused");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AStoneWithNoLibertiesIsCaptured()
    {
        GoGame game = TwoPlayers();

        // Black closes the four sides of the white stone on (1,1).
        PlayAll(game, (1, 0), (1, 1), (0, 1), (8, 8), (2, 1), (8, 7), (1, 2));

        Assert.AreEqual(GoRules.Empty, game.Board[At(1, 1)]);
        Assert.AreEqual(1, game.BlackCaptures);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SuicideIsRefused()
    {
        GoGame game = TwoPlayers();
        // White holds both neighbours of the corner; black to move may not play into it.
        PlayAll(game, (4, 4), (1, 0), (4, 5), (0, 1));

        Assert.IsFalse(game.TryPlay(At(0, 0)));
        Assert.AreEqual(GoRules.Empty, game.Board[At(0, 0)]);
        Assert.IsTrue(game.BlackToMove);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RetakingAKoAtOnceIsRefusedButAllowedAfterAnotherMove()
    {
        GoGame game = TwoPlayers();
        // White's stone on (1,1) sits among black stones; black takes it with (2,1), leaving a ko.
        PlayAll(game, (1, 0), (2, 0), (0, 1), (3, 1), (1, 2), (2, 2), (8, 0), (1, 1));
        Assert.IsTrue(game.TryPlay(At(2, 1)));
        Assert.AreEqual(1, game.BlackCaptures);

        Assert.IsFalse(game.TryPlay(At(1, 1)), "white retaking at once would repeat the position");

        PlayAll(game, (8, 8), (7, 8));
        Assert.IsTrue(game.TryPlay(At(1, 1)), "after a move elsewhere the ko may be retaken");
        Assert.AreEqual(1, game.WhiteCaptures);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TwoPassesEndTheGameAndTheDeadStonesAreMarkedByHand()
    {
        GoGame game = TwoPlayers();
        PlayAll(game, (2, 2), (6, 6));
        game.Pass();
        Assert.IsFalse(game.IsOver);
        game.Pass();

        Assert.IsTrue(game.IsOver);
        StringAssert.Contains(game.Status, "Both passed");

        game.CursorIndex = At(6, 6);
        game.Activate();
        Assert.IsTrue(game.Dead[At(6, 6)]);

        game.Activate();
        Assert.IsFalse(game.Dead[At(6, 6)]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AMoveAfterAPassResetsTheCount()
    {
        GoGame game = TwoPlayers();
        game.Pass();

        PlayAll(game, (4, 4));
        game.Pass();

        Assert.IsFalse(game.IsOver);
    }

    private static byte[] WallBoard()
    {
        byte[] board = new byte[25];
        for (int y = 0; y < 5; y++)
        {
            board[y * 5 + 1] = GoRules.Black;
            board[y * 5 + 3] = GoRules.White;
        }

        return board;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AreaScoreCountsStonesAndSurroundedEmptyPoints()
    {
        (int black, int white) = GoRules.AreaScore(WallBoard(), 5, null);

        Assert.AreEqual(10, black, "five stones and the five empty points on the left");
        Assert.AreEqual(10, white, "five stones and the five empty points on the right");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ADeadStoneIsCountedForTheSideThatSurroundsIt()
    {
        byte[] board = WallBoard();
        board[2 * 5 + 4] = GoRules.Black;
        bool[] dead = new bool[25];
        dead[2 * 5 + 4] = true;

        (int black, int white) = GoRules.AreaScore(board, 5, dead);

        Assert.AreEqual(10, black);
        Assert.AreEqual(10, white);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheSizeKeyCyclesTheBoardAndRestarts()
    {
        GoGame game = TwoPlayers();
        game.TryPlay(At(2, 2));

        Assert.IsTrue(game.TryChar('s'));

        Assert.AreEqual(13, game.Size);
        Assert.IsTrue(game.Board.All(p => p == GoRules.Empty));
        game.TryChar('s');
        Assert.AreEqual(19, game.Size);
        game.TryChar('s');
        Assert.AreEqual(9, game.Size);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UndoTakesBackTheComputersReplyToo()
    {
        GoGame game = new(new Random(4));
        game.CursorIndex = At(2, 2);
        game.Activate();
        game.PlanComputerMove()!(CancellationToken.None)!();
        Assert.IsTrue(game.BlackToMove);

        game.Undo();

        Assert.IsTrue(game.Board.All(p => p == GoRules.Empty));
        Assert.IsFalse(game.CanUndo);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheComputerCapturesAStoneInAtari()
    {
        GoGame game = new(new Random(1));
        // The black stone on (1,1) is left with the single liberty (1,2); white, the computer, is to move.
        PlayAll(game, (1, 1), (1, 0), (8, 8), (0, 1), (8, 7), (2, 1), (7, 8));
        Assert.IsTrue(game.IsComputerTurn);

        game.PlanComputerMove()!(CancellationToken.None)!();

        Assert.AreEqual(GoRules.Empty, game.Board[At(1, 1)], "the stone in atari was taken");
        Assert.AreEqual(GoRules.White, game.Board[At(1, 2)]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheComputerAnswersOnABigBoardInTime()
    {
        GoGame game = new(new Random(1));
        game.TryChar('s');
        game.TryChar('s');
        game.TryPlay(At(3, 3, 19));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        game.PlanComputerMove()!(CancellationToken.None)!();

        Assert.IsTrue(watch.ElapsedMilliseconds < 3000, $"took {watch.ElapsedMilliseconds} ms");
        Assert.IsTrue(game.BlackToMove);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheComputerPassesWhenThePersonPassedAndItIsAhead()
    {
        GoGame game = new(new Random(1));
        game.TryPlay(At(0, 0));
        game.PlanComputerMove()!(CancellationToken.None)!();
        game.Pass();
        Assert.IsTrue(game.IsComputerTurn);

        game.PlanComputerMove()!(CancellationToken.None)!();

        Assert.IsTrue(game.IsOver);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheComputerPlaysARandomGameThroughWithoutAnIllegalMove()
    {
        GoGame game = new(new Random(7)) { VsComputer = false };
        Random random = new(7);
        for (int turn = 0; turn < 120 && !game.IsOver; turn++)
        {
            int choice = GoComputer.Choose(game.Board, game.Size, game.BlackToMove ? GoRules.Black : GoRules.White, null, -1, false, turn, random, CancellationToken.None);
            if (choice < 0 || !game.TryPlay(choice))
            {
                game.Pass();
            }
        }

        Assert.IsTrue(game.Board.Count(p => p != GoRules.Empty) > 10);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheBoardDrawsTheLabelsTheGridAndTheStones()
    {
        GoGame game = TwoPlayers();
        game.TryPlay(At(4, 4));

        IReadOnlyList<GameCell[]> rows = game.Render();

        Assert.AreEqual(10, rows.Count);
        Assert.AreEqual("A ", rows[0][1].Text);
        Assert.AreEqual("●─", rows[5][5].Text);
        Assert.AreEqual(1, rows.SelectMany(r => r).Count(c => c.Cursor));
    }
}

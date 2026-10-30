using iTerminal.Core.Services.Games;

namespace iTerminal.Core.Test.Services.Games;

[TestClass]
public class NineMensMorrisTest
{
    private static NineMensMorrisGame TwoPlayers() => new(new Random(1)) { VsComputer = false };

    private static void Place(NineMensMorrisGame game, params int[] points)
    {
        foreach (int point in points)
        {
            Assert.IsTrue(game.PlayMove(new MorrisMove(-1, point, -1)), $"point {point} was refused");
        }
    }

    /// <summary>White on 0 3 5 8 10 12 14 17 23, black on 1 2 4 6 7 9 11 13 15: no mill for either, and white to move.</summary>
    private static void PlaceAllNineEach(NineMensMorrisGame game) =>
        Place(game, 0, 1, 3, 2, 5, 4, 8, 6, 10, 7, 12, 9, 14, 11, 17, 13, 23, 15);

    [TestMethod]
    public void TheBoardHas24PointsSixteenMillsAndThirtyTwoLines()
    {
        Assert.AreEqual(24, MorrisBoard.Coordinates.Length);
        Assert.AreEqual(24, MorrisBoard.Coordinates.Distinct().Count());
        Assert.AreEqual(16, MorrisBoard.Mills.Length);
        Assert.AreEqual(32, MorrisBoard.Neighbours.Sum(n => n.Length) / 2);
        Assert.IsTrue(MorrisBoard.Neighbours.All(n => n.Length is >= 2 and <= 4));
        Assert.IsTrue(MorrisBoard.Mills.All(mill => mill.Distinct().Count() == 3));
    }

    [TestMethod]
    public void ThreeInARowTakesAnOpposingMan()
    {
        NineMensMorrisGame game = TwoPlayers();
        Place(game, 0, 8, 1, 9, 2);

        Assert.IsTrue(game.MustRemove);
        Assert.IsFalse(game.TryRemove(0), "not an opposing man");
        Assert.IsTrue(game.TryRemove(8));

        Assert.IsFalse(game.MustRemove);
        Assert.AreEqual(0, game.State.Board[8]);
        Assert.AreEqual(1, game.State.Turn);
        Assert.AreEqual(8, game.State.Total(1), "one black man gone from the nine");
    }

    [TestMethod]
    public void MenInAMillAreSparedWhileOthersCanBeTaken()
    {
        byte[] board = new byte[24];
        board[0] = board[1] = board[2] = 2;
        board[10] = 2;

        List<int> removable = MorrisState.Removable(board, 1);

        CollectionAssert.AreEqual(new[] { 10 }, removable);

        board[10] = 0;
        CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, MorrisState.Removable(board, 1));
    }

    [TestMethod]
    public void AfterTheMenAreDownTheyOnlySlideToAnEmptyNeighbour()
    {
        NineMensMorrisGame game = TwoPlayers();
        PlaceAllNineEach(game);
        Assert.IsFalse(game.State.Placing(0));

        Assert.IsFalse(game.PlayMove(new MorrisMove(17, 20, -1)), "17 to 20 is not a neighbouring point");
        Assert.IsFalse(game.PlayMove(new MorrisMove(17, 9, -1)), "9 is occupied");
        Assert.IsTrue(MorrisState.Moves(game.State).All(m => MorrisBoard.Neighbours[m.From].Contains(m.To)));
        Assert.IsTrue(game.PlayMove(new MorrisMove(17, 18, -1)));
    }

    [TestMethod]
    public void AWhitePlayerDownToThreeMenMayFly()
    {
        MorrisState state = new();
        state.InHand[0] = 0;
        state.InHand[1] = 0;
        state.Board[0] = state.Board[10] = state.Board[20] = 1;
        state.Board[3] = state.Board[5] = state.Board[13] = state.Board[22] = 2;

        List<MorrisMove> moves = MorrisState.Moves(state);

        Assert.IsTrue(state.Flying(0));
        Assert.AreEqual(3 * (24 - 7), moves.Count, "each of the three men to each of the 17 empty points");
    }

    [TestMethod]
    public void ASideBelowThreeMenOrWithoutAMoveHasLost()
    {
        MorrisState few = new();
        few.InHand[0] = 0;
        few.InHand[1] = 0;
        few.Board[0] = few.Board[1] = 2;
        few.Board[10] = few.Board[11] = few.Board[12] = 1;
        Assert.AreEqual(0, few.Winner());

        MorrisState stuck = new();
        stuck.InHand[0] = 0;
        stuck.InHand[1] = 0;
        // White men on 0 and 2... walled in by black men on every neighbour; white has four men so it cannot fly.
        stuck.Turn = 0;
        foreach (int point in new[] { 0, 2, 4, 6 })
        {
            stuck.Board[point] = 1;
        }

        foreach (int point in new[] { 1, 3, 5, 7 })
        {
            stuck.Board[point] = 2;
        }

        Assert.AreEqual(1, stuck.Winner());
    }

    [TestMethod]
    public void TheCursorFollowsTheLines()
    {
        NineMensMorrisGame game = TwoPlayers();

        game.MoveCursor(1, 0);
        Assert.AreEqual(1, game.CursorPoint);
        game.MoveCursor(0, 1);
        Assert.AreEqual(9, game.CursorPoint);
        game.MoveCursor(0, 1);
        Assert.AreEqual(17, game.CursorPoint);
        game.MoveCursor(-1, 0);
        Assert.AreEqual(16, game.CursorPoint);
    }

    [TestMethod]
    public void ActivatePlacesAndThenSelectsAndMoves()
    {
        NineMensMorrisGame game = TwoPlayers();
        PlaceAllNineEach(game);
        Assert.IsFalse(game.IsOver);

        game.CursorPoint = 17;
        game.Activate();
        Assert.AreEqual(17, game.Selected);
        game.CursorPoint = 18;
        game.Activate();

        Assert.AreEqual(0, game.State.Board[17]);
        Assert.AreEqual(1, game.State.Board[18]);
    }

    [TestMethod]
    public void TheComputerFormsAMillWhenItCan()
    {
        MorrisState state = new() { Turn = 1 };
        state.InHand[0] = 7;
        state.InHand[1] = 7;
        state.Board[0] = state.Board[1] = 2;
        state.Board[10] = state.Board[20] = 1;

        MorrisMove? move = new MorrisSearch(new Random(1), CancellationToken.None, 2).Best(state);

        Assert.AreEqual(2, move!.Value.To);
        Assert.IsTrue(move.Value.Remove >= 0);
    }

    [TestMethod]
    public void TheComputerAnswersTheFirstPlacementInTime()
    {
        NineMensMorrisGame game = new(new Random(2));
        game.CursorPoint = 3;
        game.Activate();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        game.PlanComputerMove()!(CancellationToken.None)!();

        Assert.IsTrue(watch.ElapsedMilliseconds < 5000, $"took {watch.ElapsedMilliseconds} ms");
        Assert.AreEqual(0, game.State.Turn);
        Assert.AreEqual(1, game.State.OnBoard(1));
    }

    [TestMethod]
    public void TheComputerPlaysAWholeGameLegally()
    {
        NineMensMorrisGame game = new(new Random(3)) { VsComputer = false };
        Random random = new(3);
        for (int turn = 0; turn < 300 && !game.IsOver; turn++)
        {
            MorrisMove? move = new MorrisSearch(random, CancellationToken.None, 2).Best(game.State);
            Assert.IsNotNull(move);
            Assert.IsTrue(game.PlayMove(move.Value with { Remove = -1 }));
            if (move.Value.Remove >= 0)
            {
                Assert.IsTrue(game.TryRemove(move.Value.Remove));
            }
        }

        Assert.IsTrue(game.CanUndo);
    }

    [TestMethod]
    public void UndoTakesBackTheComputersReplyToo()
    {
        NineMensMorrisGame game = new(new Random(2));
        game.CursorPoint = 3;
        game.Activate();
        game.PlanComputerMove()!(CancellationToken.None)!();

        game.Undo();

        Assert.AreEqual(0, game.State.OnBoard(0) + game.State.OnBoard(1));
        Assert.IsFalse(game.CanUndo);
    }

    [TestMethod]
    public void TheBoardDrawsThirteenRowsWithTheCursor()
    {
        NineMensMorrisGame game = TwoPlayers();

        IReadOnlyList<GameCell[]> rows = game.Render();

        Assert.AreEqual(13, rows.Count);
        Assert.IsTrue(rows.All(r => r.Length == 13));
        Assert.AreEqual(1, rows.SelectMany(r => r).Count(c => c.Cursor));
        Assert.AreEqual("───", rows[0][1].Text);
        Assert.AreEqual(" │ ", rows[1][0].Text);
    }
}

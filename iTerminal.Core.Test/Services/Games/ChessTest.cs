using iTerminal.Core.Services.Games;

namespace iTerminal.Core.Test.Services.Games;

[TestClass]
public class ChessTest
{
    internal static ChessState FromFen(string fen)
    {
        string[] parts = fen.Split(' ');
        ChessState state = new();
        string[] ranks = parts[0].Split('/');
        for (int i = 0; i < 8; i++)
        {
            int rank = 7 - i;
            int file = 0;
            foreach (char c in ranks[i])
            {
                if (char.IsDigit(c))
                {
                    file += c - '0';
                    continue;
                }

                int type = "pnbrqk".IndexOf(char.ToLowerInvariant(c)) + 1;
                state.Board[rank * 8 + file++] = (sbyte)(char.IsUpper(c) ? type : -type);
            }
        }

        state.WhiteToMove = parts[1] == "w";
        state.Castling = (parts[2].Contains('K') ? 1 : 0) | (parts[2].Contains('Q') ? 2 : 0) | (parts[2].Contains('k') ? 4 : 0) | (parts[2].Contains('q') ? 8 : 0);
        state.EnPassant = parts[3] == "-" ? -1 : (parts[3][1] - '1') * 8 + (parts[3][0] - 'a');
        return state;
    }

    private static long Perft(ChessState state, int depth)
    {
        List<(ChessMove Move, ChessState Next)> successors = ChessRules.Successors(state);
        return depth == 1 ? successors.Count : successors.Sum(s => Perft(s.Next, depth - 1));
    }

    private static int Square(string name) => (name[1] - '1') * 8 + (name[0] - 'a');

    private static ChessMove Move(string text) => new(Square(text[..2]), Square(text[2..4]), text.Length > 4 ? "nbrq".IndexOf(text[4]) + 2 : 0);

    private static void Play(ChessGame game, params string[] moves)
    {
        foreach (string move in moves)
        {
            Assert.IsTrue(game.LegalMoves.Contains(Move(move)), $"{move} is not legal");
            game.Play(Move(move));
        }
    }

    [TestMethod]
    public void TheStartingPositionHasTheKnownMoveCounts()
    {
        ChessState start = ChessState.Start();

        Assert.AreEqual(20, Perft(start, 1));
        Assert.AreEqual(400, Perft(start, 2));
        Assert.AreEqual(8902, Perft(start, 3));
    }

    [TestMethod]
    public void CastlingPinsAndCapturesCountRightInTheKiwipetePosition()
    {
        ChessState state = FromFen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");

        Assert.AreEqual(48, Perft(state, 1));
        Assert.AreEqual(2039, Perft(state, 2));
    }

    [TestMethod]
    public void EnPassantAndDiscoveredChecksCountRight()
    {
        ChessState state = FromFen("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1");

        Assert.AreEqual(14, Perft(state, 1));
        Assert.AreEqual(191, Perft(state, 2));
        Assert.AreEqual(2812, Perft(state, 3));
    }

    [TestMethod]
    public void PromotionsCountRight()
    {
        ChessState state = FromFen("n1n5/PPPk4/8/8/8/8/4Kppp/5N1N b - - 0 1");

        Assert.AreEqual(24, Perft(state, 1));
        Assert.AreEqual(496, Perft(state, 2));
    }

    [TestMethod]
    public void TheFoolsMateEndsTheGame()
    {
        ChessGame game = new() { VsComputer = false };
        Play(game, "f2f3", "e7e5", "g2g4", "d8h4");

        Assert.IsTrue(game.IsOver);
        StringAssert.Contains(game.Status, "Checkmate — Black wins");
    }

    [TestMethod]
    public void AKingWithNoMoveAndNoCheckIsAStalemate()
    {
        ChessGame game = new();
        game.Load(FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1"));

        Assert.IsTrue(game.IsOver);
        StringAssert.Contains(game.Status, "Stalemate");
    }

    [TestMethod]
    public void BareKingsAreADrawByMaterial()
    {
        ChessGame game = new();
        game.Load(FromFen("4k3/8/8/8/8/8/8/3BK3 w - - 0 1"));

        Assert.IsTrue(game.IsOver);
        StringAssert.Contains(game.Status, "material");
    }

    [TestMethod]
    public void KingsideCastlingMovesTheRook()
    {
        ChessGame game = new() { VsComputer = false };
        Play(game, "e2e4", "e7e5", "g1f3", "b8c6", "f1c4", "g8f6", "e1g1");

        Assert.AreEqual(Piece.King, game.State.Board[Square("g1")]);
        Assert.AreEqual(Piece.Rook, game.State.Board[Square("f1")]);
        Assert.AreEqual(0, game.State.Board[Square("h1")]);
    }

    [TestMethod]
    public void CastlingIsNotAllowedThroughCheck()
    {
        ChessGame game = new();
        game.Load(FromFen("4kr2/8/8/8/8/8/8/4K2R w K - 0 1"));

        Assert.IsFalse(game.LegalMoves.Contains(Move("e1g1")));
    }

    [TestMethod]
    public void EnPassantTakesThePawnThatPassed()
    {
        ChessGame game = new() { VsComputer = false };
        Play(game, "e2e4", "a7a6", "e4e5", "d7d5", "e5d6");

        Assert.AreEqual(Piece.Pawn, game.State.Board[Square("d6")]);
        Assert.AreEqual(0, game.State.Board[Square("d5")], "the pawn that moved two squares is gone");
    }

    [TestMethod]
    public void APawnReachingTheLastRankAsksForThePieceAndPromotes()
    {
        ChessGame game = new() { VsComputer = false };
        game.Load(FromFen("4k3/P7/8/8/8/8/8/4K3 w - - 0 1"));

        game.CursorSquare = Square("a7");
        game.Activate();
        Assert.AreEqual(Square("a7"), game.Selected);
        game.CursorSquare = Square("a8");
        game.Activate();
        Assert.IsTrue(game.AwaitsPromotion);
        StringAssert.Contains(game.Status, "Promote");
        Assert.IsFalse(game.TryChar('x'));

        Assert.IsTrue(game.TryChar('n'));

        Assert.AreEqual(Piece.Knight, game.State.Board[Square("a8")]);
        Assert.IsFalse(game.AwaitsPromotion);
    }

    [TestMethod]
    public void ClickingAnotherOwnPieceReselectsAndTheSameSquareDeselects()
    {
        ChessGame game = new();
        game.CursorSquare = Square("e2");
        game.Activate();
        game.CursorSquare = Square("d2");
        game.Activate();
        Assert.AreEqual(Square("d2"), game.Selected);

        game.Activate();

        Assert.AreEqual(-1, game.Selected);
    }

    [TestMethod]
    public void UndoTakesBackBothMovesAgainstTheComputer()
    {
        ChessGame game = new(new Random(1));
        game.CursorSquare = Square("e2");
        game.Activate();
        game.CursorSquare = Square("e4");
        game.Activate();
        Assert.IsTrue(game.IsComputerTurn);
        game.PlanComputerMove()!(CancellationToken.None)!();
        Assert.IsFalse(game.IsComputerTurn);

        game.Undo();

        Assert.AreEqual(ChessState.Start().Key(), game.State.Key());
        Assert.IsFalse(game.CanUndo);
    }

    [TestMethod]
    public void ThePersonCannotMoveBlacksPiecesAgainstTheComputer()
    {
        ChessGame game = new(new Random(1));
        game.Play(Move("e2e4"));

        game.CursorSquare = Square("e7");
        game.Activate();

        Assert.AreEqual(-1, game.Selected);
    }

    [TestMethod]
    public void TheComputerFindsAMateInOne()
    {
        ChessState state = FromFen("6k1/5ppp/8/8/8/8/8/R3K3 w - - 0 1");

        ChessMove? move = new ChessSearch(new Random(1), CancellationToken.None, 2000).Best(state);

        Assert.AreEqual(Move("a1a8"), move);
    }

    [TestMethod]
    public void TheComputerTakesAHangingQueen()
    {
        ChessState state = FromFen("4k3/8/8/3q4/8/8/8/3RK3 w - - 0 1");

        ChessMove? move = new ChessSearch(new Random(1), CancellationToken.None, 2000).Best(state);

        Assert.AreEqual(Move("d1d5"), move);
    }

    [TestMethod]
    public void TheComputerAnswersTheOpeningWithinItsTimeBudget()
    {
        ChessGame game = new(new Random(3));
        game.Play(Move("e2e4"));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Action? reply = game.PlanComputerMove()!(CancellationToken.None);
        reply!();

        Assert.IsTrue(watch.ElapsedMilliseconds < 5000, $"took {watch.ElapsedMilliseconds} ms");
        Assert.IsTrue(game.State.WhiteToMove);
    }

    [TestMethod]
    public void AMoveChosenForAnOldPositionIsDropped()
    {
        ChessGame game = new(new Random(3));
        game.Play(Move("e2e4"));
        Action? reply = game.PlanComputerMove()!(CancellationToken.None);

        game.Undo();
        reply!();

        Assert.AreEqual(ChessState.Start().Key(), game.State.Key());
    }

    [TestMethod]
    public void ThreefoldRepetitionIsADraw()
    {
        ChessGame game = new() { VsComputer = false };
        Play(game, "g1f3", "g8f6", "f3g1", "f6g8", "g1f3", "g8f6", "f3g1", "f6g8");

        Assert.IsTrue(game.IsOver);
        StringAssert.Contains(game.Status, "repetition");
    }

    [TestMethod]
    public void TheBoardDrawsTheEightRanksAndTheFileLabels()
    {
        ChessGame game = new();

        IReadOnlyList<GameCell[]> rows = game.Render();

        Assert.AreEqual(9, rows.Count);
        Assert.AreEqual(" R ", rows[7][1].Text);
        Assert.AreEqual(" k ", rows[0][5].Text);
        Assert.AreEqual(1, rows.SelectMany(r => r).Count(c => c.Cursor));
    }

    [TestMethod]
    public void TheEnPassantSquare_MakesPositionsDifferOnlyWhenAPawnCanUseIt()
    {
        // Nothing can answer the double step, so the position is the one without it.
        Assert.AreEqual(
            FromFen("4k3/8/8/8/4P3/8/8/4K3 b - - 0 1").Key(),
            FromFen("4k3/8/8/8/4P3/8/8/4K3 b - e3 0 1").Key());

        // A black pawn beside it could take en passant, which makes it a different position.
        Assert.AreNotEqual(
            FromFen("4k3/8/8/8/3pP3/8/8/4K3 b - - 0 1").Key(),
            FromFen("4k3/8/8/8/3pP3/8/8/4K3 b - e3 0 1").Key());
    }

    [TestMethod]
    public void BishopsOnOneColourAloneCannotMate_ButMixedMinorsCan()
    {
        Assert.IsTrue(ChessRules.InsufficientMaterial(FromFen("5b2/4k3/8/8/8/8/8/2B1K3 w - - 0 1")), "c1 and f8 are both dark squares");
        Assert.IsFalse(ChessRules.InsufficientMaterial(FromFen("4bk2/8/8/8/8/8/8/2B1K3 w - - 0 1")), "c1 is dark and e8 is light");
        Assert.IsFalse(ChessRules.InsufficientMaterial(FromFen("4k3/8/8/8/8/8/8/2B1K1n1 w - - 0 1")), "a bishop and a knight");
    }

}

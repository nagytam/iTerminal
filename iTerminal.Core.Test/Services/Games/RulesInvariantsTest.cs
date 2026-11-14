using iTerminal.Core.Services.Games;

namespace iTerminal.Core.Test.Services.Games;

/// <summary>
/// Checks the rules against facts that do not depend on the games' own code paths: chess move counts from known positions
/// (perft), and invariants that random games must keep (men and stones are never created, a side's stones have liberties,
/// the computer never loses at Tic-Tac-Toe).
/// </summary>
[TestClass]
public class RulesInvariantsTest
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string Kiwipete = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";
    private const string Endgame = "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1";
    private const string Promotions = "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1";
    private const string Middlegame = "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8";

    [TestMethod]
    public void PerftFromTheStartingPositionMatchesThePublishedCounts()
    {
        ChessState state = FromFen(Start);

        Assert.AreEqual(20, Perft(state, 1));
        Assert.AreEqual(400, Perft(state, 2));
        Assert.AreEqual(8_902, Perft(state, 3));
        Assert.AreEqual(197_281, Perft(state, 4));
    }

    [TestMethod]
    public void PerftWithCastlingAndPinsInKiwipeteMatchesThePublishedCounts()
    {
        ChessState state = FromFen(Kiwipete);

        Assert.AreEqual(48, Perft(state, 1));
        Assert.AreEqual(2_039, Perft(state, 2));
        Assert.AreEqual(97_862, Perft(state, 3));
    }

    [TestMethod]
    public void PerftWithEnPassantAndKingsideRookPlaysMatchesThePublishedCounts()
    {
        ChessState state = FromFen(Endgame);

        Assert.AreEqual(14, Perft(state, 1));
        Assert.AreEqual(191, Perft(state, 2));
        Assert.AreEqual(2_812, Perft(state, 3));
        Assert.AreEqual(43_238, Perft(state, 4));
    }

    [TestMethod]
    public void PerftWithPromotionsMatchesThePublishedCounts()
    {
        ChessState state = FromFen(Promotions);

        Assert.AreEqual(6, Perft(state, 1));
        Assert.AreEqual(264, Perft(state, 2));
        Assert.AreEqual(9_467, Perft(state, 3));
    }

    [TestMethod]
    public void PerftInAMiddlegameWithPromotionCapturesMatchesThePublishedCounts()
    {
        ChessState state = FromFen(Middlegame);

        Assert.AreEqual(44, Perft(state, 1));
        Assert.AreEqual(1_486, Perft(state, 2));
        Assert.AreEqual(62_379, Perft(state, 3));
    }

    [TestMethod]
    public void FoolsMateIsReportedAsCheckmate()
    {
        ChessGame game = new(new Random(1)) { VsComputer = false };
        // f3 e5 g4 Qh4#
        game.Play(new ChessMove(Square("f2"), Square("f3")));
        game.Play(new ChessMove(Square("e7"), Square("e5")));
        game.Play(new ChessMove(Square("g2"), Square("g4")));
        game.Play(new ChessMove(Square("d8"), Square("h4")));

        Assert.IsTrue(game.IsOver);
        StringAssert.StartsWith(game.Result, "Checkmate");
    }

    [TestMethod]
    public void RandomGamesOfGoNeverLeaveAGroupWithoutLiberties()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            Random random = new(seed);
            GoGame game = new(new Random(seed)) { VsComputer = false };
            game.NewGame(9);
            int placed = 0;

            for (int ply = 0; ply < 250 && !game.IsOver; ply++)
            {
                if (random.Next(12) == 0)
                {
                    game.Pass();
                    continue;
                }

                int index = random.Next(81);
                int before = game.Board.Count(b => b != 0);
                if (game.TryPlay(index))
                {
                    placed++;
                }

                AssertEveryGroupBreathes(game.Board, 9);
                Assert.IsTrue(game.Board.Count(b => b != 0) <= before + 1);
            }

            int stones = game.Board.Count(b => b != 0);
            Assert.AreEqual(placed, stones + game.BlackCaptures + game.WhiteCaptures, "every stone played is on the board or was captured");
        }
    }

    [TestMethod]
    public void RandomGamesOfMorrisNeverCreateMen()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            Random random = new(seed);
            NineMensMorrisGame game = new(new Random(seed)) { VsComputer = false };
            int men = 18;

            for (int ply = 0; ply < 400 && !game.IsOver; ply++)
            {
                List<MorrisMove> moves = MorrisState.Moves(game.State);
                Assert.IsTrue(moves.Count > 0, "a side with no move is a finished game");
                MorrisMove move = moves[random.Next(moves.Count)];

                Assert.IsTrue(game.PlayMove(new MorrisMove(move.From, move.To, -1)));
                if (game.MustRemove)
                {
                    Assert.IsTrue(game.TryRemove(move.Remove));
                }

                int now = game.State.Total(0) + game.State.Total(1);
                Assert.IsTrue(now <= men, "no man is created");
                Assert.IsTrue(now >= men - 1, "a mill takes exactly one man");
                men = now;
            }
        }
    }

    [TestMethod]
    public void TheComputerNeverLosesAtTicTacToe()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            Random random = new(seed);
            char[] board = new string(' ', 9).ToCharArray();
            char side = 'X';
            while (true)
            {
                if (Winner(board) is { } winner)
                {
                    Assert.AreNotEqual('X', winner, "the computer, as O, must not lose");
                    break;
                }

                if (board.All(c => c != ' '))
                {
                    break;
                }

                int move = side == 'O'
                    ? TicTacToeGame.BestMove(board, 'O', random)
                    : PickEmpty(board, random);
                board[move] = side;
                side = side == 'X' ? 'O' : 'X';
            }
        }
    }

    private static readonly int[][] Lines =
    [
        [0, 1, 2], [3, 4, 5], [6, 7, 8],
        [0, 3, 6], [1, 4, 7], [2, 5, 8],
        [0, 4, 8], [2, 4, 6],
    ];

    private static char? Winner(char[] board) =>
        Lines.Where(line => board[line[0]] != ' ' && board[line[0]] == board[line[1]] && board[line[1]] == board[line[2]])
            .Select(line => (char?)board[line[0]])
            .FirstOrDefault();

    private static int PickEmpty(char[] board, Random random)
    {
        List<int> empty = [.. Enumerable.Range(0, 9).Where(i => board[i] == ' ')];
        return empty[random.Next(empty.Count)];
    }

    private static void AssertEveryGroupBreathes(byte[] board, int size)
    {
        for (int i = 0; i < board.Length; i++)
        {
            if (board[i] != GoRules.Empty)
            {
                Assert.IsTrue(GoRules.Group(board, size, i).Liberties.Count > 0, $"the stone at {i} has no liberties");
            }
        }
    }

    private static int Square(string name) => (name[1] - '1') * 8 + (name[0] - 'a');

    private static long Perft(ChessState state, int depth)
    {
        if (depth == 0)
        {
            return 1;
        }

        long nodes = 0;
        foreach (ChessMove move in ChessRules.LegalMoves(state))
        {
            nodes += depth == 1 ? 1 : Perft(ChessRules.Apply(state, move), depth - 1);
        }

        return nodes;
    }

    private static ChessState FromFen(string fen)
    {
        string[] parts = fen.Split(' ');
        ChessState state = new();
        int rank = 7;
        int file = 0;
        foreach (char c in parts[0])
        {
            if (c == '/')
            {
                rank--;
                file = 0;
            }
            else if (char.IsDigit(c))
            {
                file += c - '0';
            }
            else
            {
                int type = char.ToLowerInvariant(c) switch
                {
                    'p' => Piece.Pawn,
                    'n' => Piece.Knight,
                    'b' => Piece.Bishop,
                    'r' => Piece.Rook,
                    'q' => Piece.Queen,
                    _ => Piece.King,
                };
                state.Board[rank * 8 + file] = (sbyte)(char.IsUpper(c) ? type : -type);
                file++;
            }
        }

        state.WhiteToMove = parts[1] == "w";
        state.Castling = parts[2].Aggregate(0, (bits, c) => bits | c switch
        {
            'K' => 1,
            'Q' => 2,
            'k' => 4,
            'q' => 8,
            _ => 0,
        });
        state.EnPassant = parts[3] == "-" ? -1 : Square(parts[3]);
        return state;
    }

    [TestMethod]
    public void RandomPlayOnATetrisBoardAccountsForEveryFilledCell()
    {
        for (int seed = 0; seed < 10; seed++)
        {
            Random random = new(seed);
            TetrisGame game = new(new Random(seed)) { VsComputer = false };
            game.Activate();

            for (int step = 0; step < 3000 && !game.IsOver; step++)
            {
                switch (random.Next(6))
                {
                    case 0: game.MoveCursor(-1, 0); break;
                    case 1: game.MoveCursor(1, 0); break;
                    case 2: game.MoveCursor(0, 1); break;
                    case 3: game.MoveCursor(0, -1); break;
                    case 4: game.TryChar('d'); break;
                }

                game.Tick(TimeSpan.FromMilliseconds(100));
                AssertTetrisAccounts(game);
            }
        }
    }

    [TestMethod]
    public void TheComputersTetrisBoardAccountsForEveryFilledCellToo()
    {
        TetrisGame game = new(new Random(3));
        game.Activate();
        for (int step = 0; step < 4000 && !game.IsOver; step++)
        {
            game.Tick(TimeSpan.FromMilliseconds(100));
            AssertTetrisAccounts(game.Rival!);
        }
    }

    private static void AssertTetrisAccounts(TetrisGame game)
    {
        int filled = 0;
        for (int y = 0; y < TetrisGame.Rows; y++)
        {
            for (int x = 0; x < TetrisGame.Columns; x++)
            {
                filled += game.IsFilled(x, y) ? 1 : 0;
            }
        }

        Assert.AreEqual(4 * game.PiecesLocked - 10 * game.Lines, filled, "four cells a piece, ten a cleared row");
        if (!game.IsOver)
        {
            Assert.IsTrue(game.PieceCells.All(c => !game.IsFilled(c.X, c.Y)), "the falling piece never sits in the stack");
        }
    }

    [TestMethod]
    public void RandomSnakesOnTheSameBoardNeverShareACell()
    {
        for (int seed = 0; seed < 10; seed++)
        {
            Random random = new(seed);
            SnakeGame game = new(new Random(seed));
            game.MoveCursor(1, 0);

            for (int step = 0; step < 2000 && !game.IsOver; step++)
            {
                (int dx, int dy)[] turns = [(1, 0), (0, 1), (-1, 0), (0, -1), (0, 0)];
                (int dx, int dy) = turns[random.Next(turns.Length)];
                game.MoveCursor(dx, dy);
                game.Tick(TimeSpan.FromMilliseconds(120));

                if (!game.IsOver)
                {
                    List<(int X, int Y)> cells = [.. game.Body, .. game.Rival];
                    Assert.AreEqual(cells.Count, cells.Distinct().Count(), "the two snakes never share a cell");
                    Assert.IsTrue(cells.All(c => c.X is >= 0 and < 20 && c.Y is >= 0 and < 12));
                }
            }
        }
    }

    [TestMethod]
    public void EveryCardIsAccountedForInEveryPlayOfAJassHand()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            JassGame game = new(new Random(seed)) { ComputerPause = TimeSpan.Zero };
            int guard = 0;
            while (game.TricksPlayed < 9)
            {
                Assert.IsTrue(++guard < 2000, "the hand stalled");
                Assert.AreEqual(36, game.Hands.Sum(h => h.Count) + 4 * game.TricksPlayed + game.Trick.Count, "a card is never created or lost");

                if (game.IsComputerTurn)
                {
                    game.PlanComputerMove()!(CancellationToken.None)?.Invoke();
                }
                else if (game.Trump is null)
                {
                    Assert.IsTrue(game.TryChar(SuitOf(JassComputer.BestTrump(game.Hands[0]))));
                }
                else
                {
                    int index = JassComputer.ChooseCard(game.Hands[0], game.Trick, game.Trump.Value, 0);
                    Assert.IsTrue(index >= 0 && game.TryChar((char)('1' + index)));
                }
            }

            Assert.AreEqual(157, game.TeamOneScore + game.TeamTwoScore, $"seed {seed}");
        }
    }

    [TestMethod]
    public void RandomChessGamesNeverLeaveAKingInCheckOrLoseMaterial()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            Random random = new(seed);
            ChessState state = ChessState.Start();
            int material = CountPieces(state);

            for (int ply = 0; ply < 300; ply++)
            {
                List<ChessMove> moves = ChessRules.LegalMoves(state);
                if (moves.Count == 0)
                {
                    break;
                }

                ChessMove move = moves[random.Next(moves.Count)];
                bool mover = state.WhiteToMove;
                state = ChessRules.Apply(state, move);

                int mine = state.KingSquare(mover);
                Assert.IsTrue(mine >= 0 && !ChessRules.IsAttacked(state.Board, mine, !mover), "a legal move never leaves its own king in check");
                Assert.AreEqual(2, state.Board.Count(p => Math.Abs(p) == Piece.King));
                int now = CountPieces(state);
                Assert.IsTrue(now <= material, "no piece is created");
                material = now;
            }
        }
    }

    private static int CountPieces(ChessState state) => state.Board.Count(p => p != 0);

    private static char SuitOf(JassSuit suit) => suit switch
    {
        JassSuit.Clubs => 'C',
        JassSuit.Diamonds => 'D',
        JassSuit.Hearts => 'H',
        _ => 'S',
    };

    [TestMethod]
    public void GoForbidsASuicideThatCapturesNothing()
    {
        // White on (1,0) and (0,1) still has liberties at (1,1) and (0,2); black on the corner would have none, and captures nothing.
        byte[] board = new byte[81];
        board[1] = GoRules.White;
        board[9] = GoRules.White;
        board[2] = GoRules.Black;

        Assert.IsFalse(GoRules.TryPlace(board, 9, GoRules.Black, 0, null, out _, out _));
        Assert.IsTrue(GoRules.TryPlace(board, 9, GoRules.White, 0, null, out _, out _), "white may play there, it is not suicide for white's neighbours");
    }

    [TestMethod]
    public void GoCapturesAStoneWithItsLastLiberty()
    {
        byte[] board = new byte[81];
        board[0] = GoRules.White;
        board[1] = GoRules.Black;

        Assert.IsTrue(GoRules.TryPlace(board, 9, GoRules.Black, 80, null, out _, out int captured));
        Assert.AreEqual(0, captured, "the white stone still has a liberty at (0,1)");

        Assert.IsTrue(GoRules.TryPlace(board, 9, GoRules.Black, 9, null, out byte[] result, out captured));
        Assert.AreEqual(1, captured);
        Assert.AreEqual(GoRules.Empty, result[0]);
    }

    [TestMethod]
    public void GoRefusesToRestoreThePreviousPositionByKo()
    {
        byte[] board = new byte[81];
        board[0] = GoRules.White;
        board[1] = GoRules.Black;
        board[9] = GoRules.Black;

        // Black captures at (0,0)'s neighbour: the position after that capture is what white may not recreate at once.
        Assert.IsTrue(GoRules.TryPlace(board, 9, GoRules.Black, 18, null, out byte[] afterBlack, out _));
        Assert.IsFalse(GoRules.TryPlace(afterBlack, 9, GoRules.White, 0, board, out _, out _), "recreating the position before the last move is ko");
    }

    [TestMethod]
    public void GoAreaScoreCountsTheStonesAndTheEmptyPointsOnlyOneColourSurrounds()
    {
        byte[] board = new byte[81];
        for (int y = 0; y < 9; y++)
        {
            board[y * 9] = GoRules.Black;
        }

        (int black, int white) = GoRules.AreaScore(board, 9, null);

        Assert.AreEqual(81, black);
        Assert.AreEqual(0, white);
    }

    [TestMethod]
    public void AMorrisSideWithNoLegalMoveLoses()
    {
        MorrisState state = new();
        // Black (side 1) has four men in a row along the top, and every point they could slide to is White's.
        foreach (int point in new[] { 0, 1, 2, 3 })
        {
            state.Board[point] = state.Color(1);
        }

        foreach (int point in new[] { 7, 9, 4, 11 })
        {
            state.Board[point] = state.Color(0);
        }

        state.InHand[0] = 0;
        state.InHand[1] = 0;
        state.Turn = 1;

        Assert.AreEqual(0, state.Winner(), "the side to move cannot move, so White wins");
    }

    [TestMethod]
    public void AMorrisMillTakesAManOutsideAMillWhenThereIsOne()
    {
        byte[] board = new byte[24];
        // Side 1's men on the top-left mill and one loose man at point 9: only the loose man may go.
        foreach (int point in new[] { 0, 1, 2, 9 })
        {
            board[point] = 2;
        }

        List<int> removable = MorrisState.Removable(board, 1);
        Assert.AreEqual(1, removable.Count);
        Assert.AreEqual(9, removable[0]);
    }

    [TestMethod]
    public void AChessStalemateIsADraw()
    {
        ChessGame game = new(new Random(1)) { VsComputer = false };
        game.Load(FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1"));

        Assert.IsTrue(game.IsOver);
        StringAssert.StartsWith(game.Result, "Stalemate");
    }

    [TestMethod]
    public void KingAgainstKingIsADrawByInsufficientMaterial()
    {
        ChessGame game = new(new Random(1)) { VsComputer = false };
        game.Load(FromFen("8/8/8/4k3/8/8/8/4K3 w - - 0 1"));

        Assert.IsTrue(game.IsOver);
        StringAssert.Contains(game.Result, "material");
    }
}

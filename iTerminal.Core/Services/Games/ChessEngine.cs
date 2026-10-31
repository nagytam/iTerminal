using System.Diagnostics;
using System.Text;

namespace iTerminal.Core.Services.Games;

/// <summary>Piece types; a white piece is stored as the positive number, a black one as the negative.</summary>
internal static class Piece
{
    public const int Pawn = 1;
    public const int Knight = 2;
    public const int Bishop = 3;
    public const int Rook = 4;
    public const int Queen = 5;
    public const int King = 6;

    public static char Letter(int piece) => (piece > 0 ? char.ToUpperInvariant(Of(Math.Abs(piece))) : Of(Math.Abs(piece)));

    private static char Of(int type) => type switch
    {
        Pawn => 'p',
        Knight => 'n',
        Bishop => 'b',
        Rook => 'r',
        Queen => 'q',
        King => 'k',
        _ => ' ',
    };
}

/// <summary>A move by square number (rank * 8 + file, a1 = 0); <see cref="Promotion"/> is the piece type a pawn becomes, or 0.</summary>
internal readonly record struct ChessMove(int From, int To, int Promotion = 0)
{
    public override string ToString() => $"{ChessState.SquareName(From)}-{ChessState.SquareName(To)}{(Promotion == 0 ? string.Empty : char.ToUpperInvariant(Piece.Letter(Promotion)).ToString())}";
}

/// <summary>A chess position. Castling is a bit set: 1 white king side, 2 white queen side, 4 black king side, 8 black queen side.</summary>
internal sealed class ChessState
{
    public sbyte[] Board { get; private init; } = new sbyte[64];
    public bool WhiteToMove { get; set; } = true;
    public int Castling { get; set; } = 15;
    public int EnPassant { get; set; } = -1;
    public int HalfMove { get; set; }
    public int FullMove { get; set; } = 1;

    public static ChessState Start()
    {
        ChessState state = new();
        int[] back = [Piece.Rook, Piece.Knight, Piece.Bishop, Piece.Queen, Piece.King, Piece.Bishop, Piece.Knight, Piece.Rook];
        for (int file = 0; file < 8; file++)
        {
            state.Board[file] = (sbyte)back[file];
            state.Board[8 + file] = Piece.Pawn;
            state.Board[48 + file] = -Piece.Pawn;
            state.Board[56 + file] = (sbyte)-back[file];
        }

        return state;
    }

    public ChessState Clone() => new()
    {
        Board = (sbyte[])Board.Clone(),
        WhiteToMove = WhiteToMove,
        Castling = Castling,
        EnPassant = EnPassant,
        HalfMove = HalfMove,
        FullMove = FullMove,
    };

    /// <summary>What makes two positions the same for the threefold-repetition rule.</summary>
    public string Key()
    {
        StringBuilder builder = new(80);
        foreach (sbyte piece in Board)
        {
            builder.Append((char)('a' + piece + 6));
        }

        return builder.Append(WhiteToMove ? 'w' : 'b').Append(Castling).Append(EnPassantUsable() ? EnPassant : -1).ToString();
    }

    /// <summary>
    /// Whether a pawn of the side to move stands beside the pawn that just advanced two squares. Only then does the
    /// en passant square make the position different for the repetition rule: after a double step that nothing can
    /// answer, the position is the same as it would be a move later without the step.
    /// </summary>
    private bool EnPassantUsable()
    {
        if (EnPassant < 0)
        {
            return false;
        }

        int file = EnPassant & 7;
        int pawnSquare = EnPassant + (WhiteToMove ? -8 : 8);
        sbyte pawn = (sbyte)(WhiteToMove ? Piece.Pawn : -Piece.Pawn);
        return (file > 0 && Board[pawnSquare - 1] == pawn) || (file < 7 && Board[pawnSquare + 1] == pawn);
    }

    public static string SquareName(int square) => $"{(char)('a' + (square & 7))}{(square >> 3) + 1}";

    public int KingSquare(bool white) => Array.IndexOf(Board, (sbyte)(white ? Piece.King : -Piece.King));
}

internal static class ChessRules
{
    private static readonly (int File, int Rank)[] KnightSteps = [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];
    private static readonly (int File, int Rank)[] KingSteps = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];
    private static readonly (int File, int Rank)[] Diagonals = [(1, 1), (-1, 1), (-1, -1), (1, -1)];
    private static readonly (int File, int Rank)[] Straights = [(1, 0), (0, 1), (-1, 0), (0, -1)];

    private static bool OnBoard(int file, int rank) => (uint)file < 8 && (uint)rank < 8;

    public static bool IsAttacked(sbyte[] board, int square, bool byWhite)
    {
        int file = square & 7;
        int rank = square >> 3;
        int sign = byWhite ? 1 : -1;

        int pawnRank = byWhite ? rank - 1 : rank + 1;
        for (int pawnFile = file - 1; pawnFile <= file + 1; pawnFile += 2)
        {
            if (OnBoard(pawnFile, pawnRank) && board[pawnRank * 8 + pawnFile] == sign * Piece.Pawn)
            {
                return true;
            }
        }

        foreach ((int df, int dr) in KnightSteps)
        {
            if (OnBoard(file + df, rank + dr) && board[(rank + dr) * 8 + file + df] == sign * Piece.Knight)
            {
                return true;
            }
        }

        foreach ((int df, int dr) in KingSteps)
        {
            if (OnBoard(file + df, rank + dr) && board[(rank + dr) * 8 + file + df] == sign * Piece.King)
            {
                return true;
            }
        }

        return SliderAttacks(board, file, rank, Diagonals, sign * Piece.Bishop, sign * Piece.Queen)
            || SliderAttacks(board, file, rank, Straights, sign * Piece.Rook, sign * Piece.Queen);
    }

    private static bool SliderAttacks(sbyte[] board, int file, int rank, (int File, int Rank)[] directions, int piece, int queen)
    {
        foreach ((int df, int dr) in directions)
        {
            for (int f = file + df, r = rank + dr; OnBoard(f, r); f += df, r += dr)
            {
                int found = board[r * 8 + f];
                if (found == 0)
                {
                    continue;
                }

                if (found == piece || found == queen)
                {
                    return true;
                }

                break;
            }
        }

        return false;
    }

    public static bool InCheck(ChessState state) =>
        state.KingSquare(state.WhiteToMove) is int king && king >= 0 && IsAttacked(state.Board, king, !state.WhiteToMove);

    /// <summary>The legal moves of the side to move, each with the position it leads to.</summary>
    public static List<(ChessMove Move, ChessState Next)> Successors(ChessState state, bool capturesOnly = false)
    {
        List<ChessMove> pseudo = new(48);
        Pseudo(state, pseudo, capturesOnly);
        List<(ChessMove, ChessState)> legal = new(pseudo.Count);
        foreach (ChessMove move in pseudo)
        {
            ChessState next = Apply(state, move);
            int king = next.KingSquare(state.WhiteToMove);
            if (king >= 0 && !IsAttacked(next.Board, king, !state.WhiteToMove))
            {
                legal.Add((move, next));
            }
        }

        return legal;
    }

    public static List<ChessMove> LegalMoves(ChessState state) => [.. Successors(state).Select(s => s.Move)];

    private static void Pseudo(ChessState state, List<ChessMove> moves, bool capturesOnly)
    {
        sbyte[] board = state.Board;
        bool white = state.WhiteToMove;
        int sign = white ? 1 : -1;
        for (int square = 0; square < 64; square++)
        {
            int piece = board[square];
            if (piece == 0 || Math.Sign(piece) != sign)
            {
                continue;
            }

            int file = square & 7;
            int rank = square >> 3;
            switch (Math.Abs(piece))
            {
                case Piece.Pawn:
                    PawnMoves(state, square, file, rank, moves, capturesOnly);
                    break;
                case Piece.Knight:
                    StepMoves(board, sign, square, file, rank, KnightSteps, moves, capturesOnly);
                    break;
                case Piece.King:
                    StepMoves(board, sign, square, file, rank, KingSteps, moves, capturesOnly);
                    if (!capturesOnly)
                    {
                        Castles(state, square, moves);
                    }

                    break;
                case Piece.Bishop:
                    SlideMoves(board, sign, square, file, rank, Diagonals, moves, capturesOnly);
                    break;
                case Piece.Rook:
                    SlideMoves(board, sign, square, file, rank, Straights, moves, capturesOnly);
                    break;
                default:
                    SlideMoves(board, sign, square, file, rank, Diagonals, moves, capturesOnly);
                    SlideMoves(board, sign, square, file, rank, Straights, moves, capturesOnly);
                    break;
            }
        }
    }

    private static void PawnMoves(ChessState state, int square, int file, int rank, List<ChessMove> moves, bool capturesOnly)
    {
        sbyte[] board = state.Board;
        bool white = state.WhiteToMove;
        int direction = white ? 1 : -1;
        int promotionRank = white ? 7 : 0;
        int nextRank = rank + direction;
        if (nextRank is < 0 or > 7)
        {
            return;
        }

        int ahead = nextRank * 8 + file;
        if (board[ahead] == 0 && (!capturesOnly || nextRank == promotionRank))
        {
            AddPawnMove(moves, square, ahead, nextRank == promotionRank);
            int startRank = white ? 1 : 6;
            if (!capturesOnly && rank == startRank && board[ahead + direction * 8] == 0)
            {
                moves.Add(new ChessMove(square, ahead + direction * 8));
            }
        }

        for (int targetFile = file - 1; targetFile <= file + 1; targetFile += 2)
        {
            if (!OnBoard(targetFile, nextRank))
            {
                continue;
            }

            int target = nextRank * 8 + targetFile;
            if ((board[target] != 0 && Math.Sign(board[target]) != Math.Sign(direction)) || target == state.EnPassant)
            {
                AddPawnMove(moves, square, target, nextRank == promotionRank);
            }
        }
    }

    private static void AddPawnMove(List<ChessMove> moves, int from, int to, bool promotes)
    {
        if (!promotes)
        {
            moves.Add(new ChessMove(from, to));
            return;
        }

        foreach (int type in new[] { Piece.Queen, Piece.Rook, Piece.Bishop, Piece.Knight })
        {
            moves.Add(new ChessMove(from, to, type));
        }
    }

    private static void StepMoves(sbyte[] board, int sign, int square, int file, int rank, (int File, int Rank)[] steps, List<ChessMove> moves, bool capturesOnly)
    {
        foreach ((int df, int dr) in steps)
        {
            int f = file + df;
            int r = rank + dr;
            if (!OnBoard(f, r))
            {
                continue;
            }

            int target = board[r * 8 + f];
            if (target == 0 ? !capturesOnly : Math.Sign(target) != sign)
            {
                moves.Add(new ChessMove(square, r * 8 + f));
            }
        }
    }

    private static void SlideMoves(sbyte[] board, int sign, int square, int file, int rank, (int File, int Rank)[] directions, List<ChessMove> moves, bool capturesOnly)
    {
        foreach ((int df, int dr) in directions)
        {
            for (int f = file + df, r = rank + dr; OnBoard(f, r); f += df, r += dr)
            {
                int target = board[r * 8 + f];
                if (target == 0)
                {
                    if (!capturesOnly)
                    {
                        moves.Add(new ChessMove(square, r * 8 + f));
                    }

                    continue;
                }

                if (Math.Sign(target) != sign)
                {
                    moves.Add(new ChessMove(square, r * 8 + f));
                }

                break;
            }
        }
    }

    private static void Castles(ChessState state, int square, List<ChessMove> moves)
    {
        sbyte[] board = state.Board;
        bool white = state.WhiteToMove;
        int home = white ? 4 : 60;
        if (square != home)
        {
            return;
        }

        int kingSide = white ? 1 : 4;
        int queenSide = white ? 2 : 8;
        int rook = white ? Piece.Rook : -Piece.Rook;
        bool enemyWhite = !white;
        if (IsAttacked(board, home, enemyWhite))
        {
            return;
        }

        if ((state.Castling & kingSide) != 0 && board[home + 1] == 0 && board[home + 2] == 0 && board[home + 3] == rook
            && !IsAttacked(board, home + 1, enemyWhite) && !IsAttacked(board, home + 2, enemyWhite))
        {
            moves.Add(new ChessMove(home, home + 2));
        }

        if ((state.Castling & queenSide) != 0 && board[home - 1] == 0 && board[home - 2] == 0 && board[home - 3] == 0 && board[home - 4] == rook
            && !IsAttacked(board, home - 1, enemyWhite) && !IsAttacked(board, home - 2, enemyWhite))
        {
            moves.Add(new ChessMove(home, home - 2));
        }
    }

    /// <summary>The position after the move, which is taken to be pseudo-legal.</summary>
    public static ChessState Apply(ChessState state, ChessMove move)
    {
        ChessState next = state.Clone();
        sbyte[] board = next.Board;
        int piece = board[move.From];
        int type = Math.Abs(piece);
        bool white = piece > 0;
        int captured = board[move.To];

        board[move.From] = 0;
        if (type == Piece.Pawn && move.To == state.EnPassant && captured == 0 && (move.From & 7) != (move.To & 7))
        {
            board[move.To + (white ? -8 : 8)] = 0;
            captured = -piece;
        }

        board[move.To] = move.Promotion != 0 ? (sbyte)((white ? 1 : -1) * move.Promotion) : (sbyte)piece;

        if (type == Piece.King && Math.Abs((move.To & 7) - (move.From & 7)) == 2)
        {
            int rookFrom = move.To > move.From ? move.To + 1 : move.To - 2;
            int rookTo = move.To > move.From ? move.To - 1 : move.To + 1;
            board[rookTo] = board[rookFrom];
            board[rookFrom] = 0;
        }

        foreach (int touched in new[] { move.From, move.To })
        {
            next.Castling &= touched switch
            {
                0 => ~2,
                7 => ~1,
                4 => ~3,
                56 => ~8,
                63 => ~4,
                60 => ~12,
                _ => ~0,
            };
        }

        next.EnPassant = type == Piece.Pawn && Math.Abs(move.To - move.From) == 16 ? (move.From + move.To) / 2 : -1;
        next.HalfMove = type == Piece.Pawn || captured != 0 ? 0 : state.HalfMove + 1;
        if (!white)
        {
            next.FullMove++;
        }

        next.WhiteToMove = !state.WhiteToMove;
        return next;
    }

    /// <summary>Neither side can mate: bare kings, or a king and a single minor piece against a bare king.</summary>
    public static bool InsufficientMaterial(ChessState state)
    {
        int minors = 0;
        foreach (sbyte piece in state.Board)
        {
            switch (Math.Abs(piece))
            {
                case 0:
                case Piece.King:
                    break;
                case Piece.Knight:
                case Piece.Bishop:
                    minors++;
                    break;
                default:
                    return false;
            }
        }

        if (minors <= 1)
        {
            return true;
        }

        // Only bishops left, all on squares of one colour: no mate is possible whoever has them.
        int bishopColours = 0;
        for (int square = 0; square < 64; square++)
        {
            switch (Math.Abs(state.Board[square]))
            {
                case Piece.Bishop:
                    bishopColours |= 1 << (((square & 7) + (square >> 3)) & 1);
                    break;
                case 0:
                case Piece.King:
                    break;
                default:
                    return false;
            }
        }

        return bishopColours is 1 or 2;
    }
}

/// <summary>Iterative-deepening alpha-beta search with a capture-only quiescence tail, held to a time budget.</summary>
internal sealed class ChessSearch(Random random, CancellationToken token, int budgetMilliseconds, int maxDepth = 4)
{
    private const int Mate = 100_000;
    private static readonly int[] Values = [0, 100, 320, 330, 500, 900, 0];

    private sealed class TimeUp : Exception;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _mayAbort;
    private int _nodes;

    /// <summary>The move the side to move should play, or null if it has none.</summary>
    public ChessMove? Best(ChessState state)
    {
        List<(ChessMove Move, ChessState Next)> root = ChessRules.Successors(state);
        if (root.Count == 0)
        {
            return null;
        }

        Order(state, root);
        ChessMove best = root[0].Move;
        for (int depth = 1; depth <= maxDepth; depth++)
        {
            _mayAbort = depth > 1;
            try
            {
                int alpha = -Mate * 2;
                ChessMove bestThisDepth = root[0].Move;
                foreach ((ChessMove move, ChessState next) in root)
                {
                    int score = -Negamax(next, depth - 1, -Mate * 2, -alpha, 1);
                    if (score > alpha)
                    {
                        alpha = score;
                        bestThisDepth = move;
                    }
                }

                best = bestThisDepth;
                int index = root.FindIndex(r => r.Move == best);
                (ChessMove, ChessState) first = root[index];
                root.RemoveAt(index);
                root.Insert(0, first);
                if (Math.Abs(alpha) > Mate - 100)
                {
                    break;
                }
            }
            catch (TimeUp)
            {
                break;
            }
        }

        return best;
    }

    private void CheckLimits()
    {
        if ((++_nodes & 1023) != 0)
        {
            return;
        }

        token.ThrowIfCancellationRequested();
        if (_mayAbort && _clock.ElapsedMilliseconds > budgetMilliseconds)
        {
            throw new TimeUp();
        }
    }

    private int Negamax(ChessState state, int depth, int alpha, int beta, int ply)
    {
        CheckLimits();
        if (state.HalfMove >= 100)
        {
            return 0;
        }

        if (depth <= 0)
        {
            return Quiesce(state, alpha, beta, 0);
        }

        List<(ChessMove Move, ChessState Next)> successors = ChessRules.Successors(state);
        if (successors.Count == 0)
        {
            return ChessRules.InCheck(state) ? -(Mate - ply) : 0;
        }

        Order(state, successors);
        foreach ((_, ChessState next) in successors)
        {
            int score = -Negamax(next, depth - 1, -beta, -alpha, ply + 1);
            if (score >= beta)
            {
                return beta;
            }

            alpha = Math.Max(alpha, score);
        }

        return alpha;
    }

    private int Quiesce(ChessState state, int alpha, int beta, int depth)
    {
        CheckLimits();
        int standPat = Evaluate(state);
        if (!state.WhiteToMove)
        {
            standPat = -standPat;
        }

        if (standPat >= beta || depth >= 6)
        {
            return standPat >= beta ? beta : Math.Max(alpha, standPat);
        }

        alpha = Math.Max(alpha, standPat);
        List<(ChessMove Move, ChessState Next)> captures = ChessRules.Successors(state, capturesOnly: true);
        Order(state, captures);
        foreach ((_, ChessState next) in captures)
        {
            int score = -Quiesce(next, -beta, -alpha, depth + 1);
            if (score >= beta)
            {
                return beta;
            }

            alpha = Math.Max(alpha, score);
        }

        return alpha;
    }

    /// <summary>Captures of the most valuable piece by the least valuable one come first, then promotions.</summary>
    private static void Order(ChessState state, List<(ChessMove Move, ChessState Next)> moves)
    {
        int Rank((ChessMove Move, ChessState Next) entry)
        {
            int victim = Math.Abs(state.Board[entry.Move.To]);
            int mover = Math.Abs(state.Board[entry.Move.From]);
            int rank = victim > 0 ? 10 * Values[victim] - Values[mover] + 10_000 : 0;
            if (entry.Move.Promotion != 0)
            {
                rank += Values[entry.Move.Promotion] + 5_000;
            }

            return rank;
        }

        moves.Sort((a, b) => Rank(b).CompareTo(Rank(a)));
    }

    /// <summary>Material plus a little placement, in centipawns, from White's side.</summary>
    internal int Evaluate(ChessState state)
    {
        int score = 0;
        int whiteBishops = 0;
        int blackBishops = 0;
        for (int square = 0; square < 64; square++)
        {
            int piece = state.Board[square];
            if (piece == 0)
            {
                continue;
            }

            int type = Math.Abs(piece);
            bool white = piece > 0;
            int file = square & 7;
            int rank = square >> 3;
            int own = white ? rank : 7 - rank;
            double centre = Math.Abs(file - 3.5) + Math.Abs(rank - 3.5);
            int value = Values[type];
            switch (type)
            {
                case Piece.Pawn:
                    value += own * 8 + (own is >= 2 and <= 5 ? (int)((3.5 - Math.Abs(file - 3.5)) * 4) : 0);
                    break;
                case Piece.Knight:
                    value += (int)((7 - centre) * 5);
                    break;
                case Piece.Bishop:
                    value += (int)((7 - centre) * 2);
                    if (white)
                    {
                        whiteBishops++;
                    }
                    else
                    {
                        blackBishops++;
                    }

                    break;
                case Piece.Rook:
                    value += own == 6 ? 20 : 0;
                    break;
                case Piece.King:
                    value += own == 0 && file is 1 or 2 or 6 ? 15 : 0;
                    break;
            }

            score += white ? value : -value;
        }

        score += (whiteBishops >= 2 ? 30 : 0) - (blackBishops >= 2 ? 30 : 0);
        return score + random.Next(4);
    }
}

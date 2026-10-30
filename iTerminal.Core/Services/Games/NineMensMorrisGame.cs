using iConsole.Display;

namespace iTerminal.Core.Services.Games;

/// <summary>The board of Nine Men's Morris: 24 points on three nested squares, with their neighbours and the 16 mills.</summary>
internal static class MorrisBoard
{
    /// <summary>Each point's (column, row) on a 7x7 grid.</summary>
    public static readonly (int X, int Y)[] Coordinates =
    [
        (0, 0), (3, 0), (6, 0), (6, 3), (6, 6), (3, 6), (0, 6), (0, 3),
        (1, 1), (3, 1), (5, 1), (5, 3), (5, 5), (3, 5), (1, 5), (1, 3),
        (2, 2), (3, 2), (4, 2), (4, 3), (4, 4), (3, 4), (2, 4), (2, 3),
    ];

    public static readonly int[][] Neighbours = BuildNeighbours();

    public static readonly int[][] Mills = BuildMills();

    /// <summary>The mills each point belongs to.</summary>
    public static readonly int[][][] MillsOf = [.. Enumerable.Range(0, 24).Select(point => Mills.Where(mill => mill.Contains(point)).ToArray())];

    private static int[][] BuildNeighbours()
    {
        List<int>[] lists = [.. Enumerable.Range(0, 24).Select(_ => new List<int>())];
        void Link(int a, int b)
        {
            lists[a].Add(b);
            lists[b].Add(a);
        }

        for (int ring = 0; ring < 3; ring++)
        {
            for (int i = 0; i < 8; i++)
            {
                Link(ring * 8 + i, ring * 8 + (i + 1) % 8);
            }
        }

        for (int middle = 1; middle < 8; middle += 2)
        {
            Link(middle, 8 + middle);
            Link(8 + middle, 16 + middle);
        }

        return [.. lists.Select(l => l.ToArray())];
    }

    private static int[][] BuildMills()
    {
        List<int[]> mills = [];
        for (int ring = 0; ring < 3; ring++)
        {
            for (int side = 0; side < 4; side++)
            {
                mills.Add([ring * 8 + side * 2, ring * 8 + side * 2 + 1, ring * 8 + (side * 2 + 2) % 8]);
            }
        }

        for (int middle = 1; middle < 8; middle += 2)
        {
            mills.Add([middle, 8 + middle, 16 + middle]);
        }

        return [.. mills];
    }

    public static bool InMill(byte[] board, int point)
    {
        byte color = board[point];
        if (color == 0)
        {
            return false;
        }

        foreach (int[] mill in MillsOf[point])
        {
            if (board[mill[0]] == color && board[mill[1]] == color && board[mill[2]] == color)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Would a man of this colour on <paramref name="to"/> complete a mill, if the man on <paramref name="from"/> (or -1) has left its point?</summary>
    public static bool FormsMill(byte[] board, int from, int to, byte color)
    {
        foreach (int[] mill in MillsOf[to])
        {
            bool complete = true;
            foreach (int point in mill)
            {
                if (point != to && (point == from || board[point] != color))
                {
                    complete = false;
                    break;
                }
            }

            if (complete)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>A morris move: <see cref="From"/> is -1 while placing, <see cref="Remove"/> is -1 when no mill was made.</summary>
internal readonly record struct MorrisMove(int From, int To, int Remove);

/// <summary>The state of a game of morris, small enough to copy for every node of the computer's search.</summary>
internal sealed class MorrisState
{
    public byte[] Board { get; private init; } = new byte[24];
    public int[] InHand { get; private init; } = [9, 9];
    public int Turn { get; set; }
    public int QuietMoves { get; set; }

    public MorrisState Clone() => new()
    {
        Board = (byte[])Board.Clone(),
        InHand = (int[])InHand.Clone(),
        Turn = Turn,
        QuietMoves = QuietMoves,
    };

    public byte Color(int side) => (byte)(side + 1);

    public int OnBoard(int side)
    {
        int count = 0;
        foreach (byte point in Board)
        {
            if (point == side + 1)
            {
                count++;
            }
        }

        return count;
    }

    public int Total(int side) => OnBoard(side) + InHand[side];

    public bool Placing(int side) => InHand[side] > 0;

    public bool Flying(int side) => !Placing(side) && OnBoard(side) == 3;

    /// <summary>The player that cannot move or has fewer than three men has lost; -1 while the game goes on, 2 for a draw.</summary>
    public int Winner()
    {
        for (int side = 0; side < 2; side++)
        {
            if (Total(side) < 3)
            {
                return 1 - side;
            }
        }

        if (!HasMoves())
        {
            return 1 - Turn;
        }

        return QuietMoves >= 100 ? 2 : -1;
    }

    /// <summary>Whether the side to move has a legal move at all, without listing them.</summary>
    private bool HasMoves()
    {
        bool placingOrFlying = Placing(Turn) || Flying(Turn);
        byte color = Color(Turn);
        for (int point = 0; point < 24; point++)
        {
            if (placingOrFlying && Board[point] == 0)
            {
                return true;
            }

            if (!placingOrFlying && Board[point] == color && MorrisBoard.Neighbours[point].Any(n => Board[n] == 0))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Every legal move of the side to move; a mill comes once for each man it may take.</summary>
    public static List<MorrisMove> Moves(MorrisState state)
    {
        List<MorrisMove> moves = [];
        int side = state.Turn;
        byte color = state.Color(side);
        if (state.Placing(side))
        {
            for (int to = 0; to < 24; to++)
            {
                if (state.Board[to] == 0)
                {
                    AddWithRemovals(state, moves, -1, to);
                }
            }

            return moves;
        }

        bool flying = state.Flying(side);
        for (int from = 0; from < 24; from++)
        {
            if (state.Board[from] != color)
            {
                continue;
            }

            if (flying)
            {
                for (int to = 0; to < 24; to++)
                {
                    if (state.Board[to] == 0)
                    {
                        AddWithRemovals(state, moves, from, to);
                    }
                }
            }
            else
            {
                foreach (int to in MorrisBoard.Neighbours[from].Where(n => state.Board[n] == 0))
                {
                    AddWithRemovals(state, moves, from, to);
                }
            }
        }

        return moves;
    }

    private static void AddWithRemovals(MorrisState state, List<MorrisMove> moves, int from, int to)
    {
        byte color = state.Color(state.Turn);
        if (!MorrisBoard.FormsMill(state.Board, from, to, color))
        {
            moves.Add(new MorrisMove(from, to, -1));
            return;
        }

        byte[] board = (byte[])state.Board.Clone();
        if (from >= 0)
        {
            board[from] = 0;
        }

        board[to] = color;

        foreach (int victim in Removable(board, 1 - state.Turn))
        {
            moves.Add(new MorrisMove(from, to, victim));
        }
    }

    /// <summary>The men of a side that may be taken: those outside a mill, or all of them if every one is in a mill.</summary>
    public static List<int> Removable(byte[] board, int side)
    {
        byte color = (byte)(side + 1);
        List<int> all = [];
        List<int> loose = [];
        for (int point = 0; point < 24; point++)
        {
            if (board[point] != color)
            {
                continue;
            }

            all.Add(point);
            if (!MorrisBoard.InMill(board, point))
            {
                loose.Add(point);
            }
        }

        return loose.Count > 0 ? loose : all;
    }

    public MorrisState Apply(MorrisMove move)
    {
        MorrisState next = Clone();
        byte color = Color(Turn);
        if (move.From >= 0)
        {
            next.Board[move.From] = 0;
        }
        else
        {
            next.InHand[Turn]--;
        }

        next.Board[move.To] = color;
        if (move.Remove >= 0)
        {
            next.Board[move.Remove] = 0;
            next.QuietMoves = 0;
        }
        else
        {
            next.QuietMoves++;
        }

        next.Turn = 1 - Turn;
        return next;
    }
}

/// <summary>Iterative-deepening alpha-beta search for the computer's morris moves, held to a time budget.</summary>
internal sealed class MorrisSearch(Random random, CancellationToken token, int maxDepth, int budgetMilliseconds = 1500)
{
    private const int Win = 100_000;

    private sealed class TimeUp : Exception;

    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private bool _mayAbort;
    private int _nodes;

    public MorrisMove? Best(MorrisState state)
    {
        List<MorrisMove> moves = [.. MorrisState.Moves(state).OrderBy(_ => random.Next())];
        if (moves.Count == 0)
        {
            return null;
        }

        MorrisMove best = moves[0];
        for (int depth = 1; depth <= maxDepth; depth++)
        {
            _mayAbort = depth > 1;
            try
            {
                List<MorrisMove> top = [];
                int topScore = int.MinValue;
                foreach (MorrisMove move in moves)
                {
                    int score = -Negamax(state.Apply(move), depth - 1, -Win * 2, Win * 2, 1);
                    if (score > topScore)
                    {
                        topScore = score;
                        top.Clear();
                    }

                    if (score == topScore)
                    {
                        top.Add(move);
                    }
                }

                best = top[random.Next(top.Count)];
                // The next, deeper pass starts with the move that looked best.
                moves.Remove(best);
                moves.Insert(0, best);
                if (Math.Abs(topScore) > Win - 100)
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

    private int Negamax(MorrisState state, int remaining, int alpha, int beta, int ply)
    {
        if ((++_nodes & 255) == 0)
        {
            token.ThrowIfCancellationRequested();
            if (_mayAbort && _clock.ElapsedMilliseconds > budgetMilliseconds)
            {
                throw new TimeUp();
            }
        }

        int winner = state.Winner();
        if (winner >= 0)
        {
            return winner == 2 ? 0 : winner == state.Turn ? Win - ply : -(Win - ply);
        }

        if (remaining <= 0)
        {
            return Evaluate(state);
        }

        // Moves that make a mill go first: they cut the search short most often.
        List<MorrisMove> moves = MorrisState.Moves(state);
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (MorrisMove move in moves)
            {
                if ((move.Remove >= 0) != (pass == 0))
                {
                    continue;
                }

                int score = -Negamax(state.Apply(move), remaining - 1, -beta, -alpha, ply + 1);
                if (score >= beta)
                {
                    return beta;
                }

                alpha = Math.Max(alpha, score);
            }
        }

        return alpha;
    }

    /// <summary>The material and the room to move, for the side to move.</summary>
    private static int Evaluate(MorrisState state)
    {
        int me = state.Turn;
        int other = 1 - me;
        int score = (state.Total(me) - state.Total(other)) * 100;
        score += (Mobility(state, me) - Mobility(state, other)) * 4;
        score += (PotentialMills(state, me) - PotentialMills(state, other)) * 12;
        return score;
    }

    private static int Mobility(MorrisState state, int side)
    {
        byte color = state.Color(side);
        int room = 0;
        for (int point = 0; point < 24; point++)
        {
            if (state.Board[point] != color)
            {
                continue;
            }

            foreach (int neighbour in MorrisBoard.Neighbours[point])
            {
                if (state.Board[neighbour] == 0)
                {
                    room++;
                }
            }
        }

        return room;
    }

    /// <summary>Mills with two of a side's men and the third point empty.</summary>
    private static int PotentialMills(MorrisState state, int side)
    {
        byte color = state.Color(side);
        int count = 0;
        foreach (int[] mill in MorrisBoard.Mills)
        {
            int own = 0;
            int empty = 0;
            foreach (int point in mill)
            {
                if (state.Board[point] == color)
                {
                    own++;
                }
                else if (state.Board[point] == 0)
                {
                    empty++;
                }
            }

            if (own == 2 && empty == 1)
            {
                count++;
            }
        }

        return count;
    }
}

/// <summary>
/// Nine Men's Morris: place nine men each, then slide them along the lines; three in a row is a mill and takes an
/// opposing man (one outside a mill, if there is one). A player down to three men may fly anywhere; below three, or unable to
/// move, loses. White moves first; against the computer you play White.
/// </summary>
public class NineMensMorrisGame : GameBase
{
    private sealed record Snapshot(MorrisState State, int Last);

    private readonly Stack<Snapshot> _history = new();
    private MorrisState _state = new();
    private int _cursor;
    private int _selected = -1;
    private bool _mustRemove;
    private int _placed = -1;
    private int _movedFrom = -1;
    private int _last = -1;
    private int _winner = -1;
    private string _message = string.Empty;

    public NineMensMorrisGame() : this(null)
    {
    }

    internal NineMensMorrisGame(Random? random) : base(random)
    {
        NewGame();
    }

    public override string Name => "Nine Men's Morris";

    public override string Rules => "Place your nine men, then slide them along the lines. Three in a row is a mill: take an opposing man. With three men left you may fly.";

    public override bool IsOver => _winner >= 0;

    public override bool CanUndo => _history.Count > 0;

    public override bool IsComputerTurn => VsComputer && !IsOver && _state.Turn == 1;

    internal MorrisState State => _state;

    internal bool MustRemove => _mustRemove;

    internal int Selected => _selected;

    internal int Winner => _winner;

    internal int CursorPoint
    {
        get => _cursor;
        set => _cursor = value;
    }

    private static string SideName(int side) => side == 0 ? "White" : "Black";

    public override string Status
    {
        get
        {
            if (_message.Length > 0)
            {
                return _message;
            }

            if (_winner == 2)
            {
                return "A draw: a hundred moves went by without a mill.";
            }

            if (_winner >= 0)
            {
                return $"{SideName(_winner)} wins!";
            }

            string side = SideName(_state.Turn);
            if (_mustRemove)
            {
                return $"{side} made a mill: take an opposing man.";
            }

            string hands = $"In hand: White {_state.InHand[0]}, Black {_state.InHand[1]}.";
            if (_state.Placing(_state.Turn))
            {
                return $"{side} to place a man. {hands}";
            }

            return $"{side} to move{(_state.Flying(_state.Turn) ? " (flying)" : string.Empty)}. {hands}";
        }
    }

    public override void NewGame()
    {
        Version++;
        _state = new MorrisState();
        _history.Clear();
        _selected = -1;
        _mustRemove = false;
        _placed = -1;
        _movedFrom = -1;
        _last = -1;
        _winner = -1;
        _message = string.Empty;
        _cursor = 0;
    }

    public override void MoveCursor(int dx, int dy)
    {
        (int x, int y) = MorrisBoard.Coordinates[_cursor];
        int best = -1;
        int bestCost = int.MaxValue;
        for (int p = 0; p < 24; p++)
        {
            (int px, int py) = MorrisBoard.Coordinates[p];
            int primary = dx != 0 ? (px - x) * dx : (py - y) * dy;
            int secondary = Math.Abs(dx != 0 ? py - y : px - x);
            if (primary <= 0)
            {
                continue;
            }

            int cost = primary + 3 * secondary;
            if (cost < bestCost)
            {
                bestCost = cost;
                best = p;
            }
        }

        if (best >= 0)
        {
            _cursor = best;
        }
    }

    public override void Activate()
    {
        _message = string.Empty;
        if (IsOver || (VsComputer && _state.Turn == 1))
        {
            return;
        }

        if (_mustRemove)
        {
            if (!TryRemove(_cursor))
            {
                _message = "Take a man outside a mill if there is one.";
            }

            return;
        }

        int side = _state.Turn;
        if (_state.Placing(side))
        {
            if (!PlayMove(new MorrisMove(-1, _cursor, -1)))
            {
                _message = "That point is taken.";
            }

            return;
        }

        if (_selected == _cursor)
        {
            _selected = -1;
        }
        else if (_state.Board[_cursor] == _state.Color(side))
        {
            _selected = _cursor;
            if (!MorrisState.Moves(_state).Any(m => m.From == _cursor))
            {
                _selected = -1;
                _message = "That man cannot move.";
            }
        }
        else if (_selected >= 0)
        {
            if (!PlayMove(new MorrisMove(_selected, _cursor, -1)))
            {
                _message = "A man slides to a neighbouring empty point.";
            }
        }
    }

    /// <summary>Plays a man (placing or moving it); a mill leaves the removal to the next step.</summary>
    internal bool PlayMove(MorrisMove move)
    {
        List<MorrisMove> legal = MorrisState.Moves(_state);
        if (IsOver || _mustRemove || !legal.Any(m => m.From == move.From && m.To == move.To))
        {
            return false;
        }

        Version++;
        _history.Push(new Snapshot(_state, _last));
        _selected = -1;
        _last = move.To;
        bool mill = legal.Any(m => m.From == move.From && m.To == move.To && m.Remove >= 0);
        if (mill)
        {
            // The man is down; the move is completed by TryRemove. Keep the pre-move position for that step.
            _placed = move.To;
            _movedFrom = move.From;
            _mustRemove = true;
            return true;
        }

        _state = _state.Apply(move);
        Finish();
        return true;
    }

    internal bool TryRemove(int point)
    {
        if (!_mustRemove)
        {
            return false;
        }

        MorrisMove move = new(_movedFrom, _placed, point);
        if (!MorrisState.Moves(_state).Contains(move))
        {
            return false;
        }

        Version++;
        _state = _state.Apply(move);
        _mustRemove = false;
        Finish();
        return true;
    }

    private void Finish() => _winner = _state.Winner();

    public override void Undo()
    {
        _message = string.Empty;
        if (_history.Count == 0)
        {
            return;
        }

        Version++;
        Restore();
        // Against the computer, take back its answer too, so it is the person's turn again.
        if (VsComputer && _state.Turn == 1 && _history.Count > 0)
        {
            Restore();
        }
    }

    private void Restore()
    {
        Snapshot snapshot = _history.Pop();
        _state = snapshot.State;
        _last = snapshot.Last;
        _mustRemove = false;
        _selected = -1;
        _winner = -1;
    }

    public override IReadOnlyList<GameCell[]> Render()
    {
        Color wood = Color.DarkYellow;
        string[,] lines = new string[13, 7 * 2 - 1];
        // Fill in every grid cell that lies on a line between two neighbouring points.
        for (int a = 0; a < 24; a++)
        {
            foreach (int b in MorrisBoard.Neighbours[a].Where(b => b > a))
            {
                (int ax, int ay) = MorrisBoard.Coordinates[a];
                (int bx, int by) = MorrisBoard.Coordinates[b];
                if (ay == by)
                {
                    for (int x = Math.Min(ax, bx) * 2 + 1; x < Math.Max(ax, bx) * 2; x++)
                    {
                        lines[ay * 2, x] = "───";
                    }
                }
                else
                {
                    for (int y = Math.Min(ay, by) * 2 + 1; y < Math.Max(ay, by) * 2; y++)
                    {
                        lines[y, ax * 2] = " │ ";
                    }
                }
            }
        }

        HashSet<int> hints = [];
        if (!_mustRemove && _selected >= 0)
        {
            hints = [.. MorrisState.Moves(_state).Where(m => m.From == _selected).Select(m => m.To)];
        }

        HashSet<int> removable = _mustRemove ? [.. MorrisState.Removable(AfterPendingMove(), 1 - _state.Turn)] : [];
        Dictionary<(int, int), int> pointAt = MorrisBoard.Coordinates.Select((c, i) => (c, i)).ToDictionary(t => t.c, t => t.i);
        byte[] board = _mustRemove ? AfterPendingMove() : _state.Board;

        List<GameCell[]> rows = [];
        for (int y = 0; y < 13; y++)
        {
            GameCell[] row = new GameCell[13];
            for (int x = 0; x < 13; x++)
            {
                GameCell cell = new("   ", Color.Black, wood);
                if (x % 2 == 0 && y % 2 == 0 && pointAt.TryGetValue((x / 2, y / 2), out int point))
                {
                    byte stone = board[point];
                    cell = stone switch
                    {
                        1 => new GameCell(" ● ", Color.White, point == _last ? Color.DarkRed : wood),
                        2 => new GameCell(" ● ", Color.Black, point == _last ? Color.DarkRed : wood),
                        _ => new GameCell(hints.Contains(point) ? " ○ " : " + ", hints.Contains(point) ? Color.Green : Color.Black, wood),
                    };
                    if (point == _selected)
                    {
                        cell = cell with { Background = Color.Green };
                    }
                    else if (removable.Contains(point))
                    {
                        cell = cell with { Background = Color.Red };
                    }

                    if (point == _cursor)
                    {
                        cell = cell.WithCursor();
                    }
                }
                else if (lines[y, x] is { } line)
                {
                    cell = new GameCell(line, Color.Black, wood);
                }

                row[x] = cell;
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>The board with the man that just made a mill already down (the state itself only changes once a man is taken).</summary>
    private byte[] AfterPendingMove()
    {
        byte[] board = (byte[])_state.Board.Clone();
        if (_movedFrom >= 0)
        {
            board[_movedFrom] = 0;
        }

        board[_placed] = _state.Color(_state.Turn);
        return board;
    }

    public override Func<CancellationToken, Action?>? PlanComputerMove()
    {
        if (!IsComputerTurn)
        {
            return null;
        }

        MorrisState snapshot = _state;
        int version = Version;
        int seed = Random.Next();
        return token =>
        {
            MorrisMove? move = new MorrisSearch(new Random(seed), token, 4).Best(snapshot);
            return move is null ? null : () =>
            {
                if (version != Version)
                {
                    return;
                }

                PlayMove(move.Value with { Remove = -1 });
                if (move.Value.Remove >= 0)
                {
                    TryRemove(move.Value.Remove);
                }
            };
        };
    }
}

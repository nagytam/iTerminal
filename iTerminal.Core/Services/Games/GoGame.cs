using iConsole.Display;

namespace iTerminal.Core.Services.Games;

/// <summary>The rules of Go that need no game state: groups, liberties, captures and area scoring.</summary>
internal static class GoRules
{
    public const byte Empty = 0;
    public const byte Black = 1;
    public const byte White = 2;

    public static byte Other(byte color) => color == Black ? White : Black;

    public static IEnumerable<int> Neighbours(int size, int index)
    {
        int x = index % size;
        int y = index / size;
        if (x > 0) yield return index - 1;
        if (x < size - 1) yield return index + 1;
        if (y > 0) yield return index - size;
        if (y < size - 1) yield return index + size;
    }

    /// <summary>The stones connected to the one at <paramref name="index"/> and the empty points next to them.</summary>
    public static (List<int> Stones, HashSet<int> Liberties) Group(byte[] board, int size, int index)
    {
        byte color = board[index];
        List<int> stones = [index];
        HashSet<int> seen = [index];
        HashSet<int> liberties = [];
        for (int i = 0; i < stones.Count; i++)
        {
            foreach (int neighbour in Neighbours(size, stones[i]))
            {
                if (board[neighbour] == Empty)
                {
                    liberties.Add(neighbour);
                }
                else if (board[neighbour] == color && seen.Add(neighbour))
                {
                    stones.Add(neighbour);
                }
            }
        }

        return (stones, liberties);
    }

    /// <summary>
    /// Plays a stone. Fails on an occupied point, on suicide, and on a move that would bring back the position before the
    /// opponent's last move (the ko rule). On success <paramref name="result"/> is the new board with the captured stones gone.
    /// </summary>
    public static bool TryPlace(byte[] board, int size, byte color, int index, byte[]? previous, out byte[] result, out int captured)
    {
        result = board;
        captured = 0;
        if (index < 0 || index >= board.Length || board[index] != Empty)
        {
            return false;
        }

        result = (byte[])board.Clone();
        result[index] = color;
        foreach (int neighbour in Neighbours(size, index))
        {
            if (result[neighbour] != Other(color))
            {
                continue;
            }

            (List<int> stones, HashSet<int> liberties) = Group(result, size, neighbour);
            if (liberties.Count == 0)
            {
                foreach (int stone in stones)
                {
                    result[stone] = Empty;
                }

                captured += stones.Count;
            }
        }

        if (Group(result, size, index).Liberties.Count == 0 || (previous is not null && result.AsSpan().SequenceEqual(previous)))
        {
            result = board;
            captured = 0;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Who owns each empty point: Black or White if the empty region around it touches only that colour, otherwise Empty.
    /// Stones flagged in <paramref name="dead"/> count as empty.
    /// </summary>
    public static byte[] Territory(byte[] board, int size, bool[]? dead)
    {
        byte[] live = (byte[])board.Clone();
        if (dead is not null)
        {
            for (int i = 0; i < live.Length; i++)
            {
                if (dead[i])
                {
                    live[i] = Empty;
                }
            }
        }

        byte[] owner = new byte[live.Length];
        bool[] visited = new bool[live.Length];
        for (int start = 0; start < live.Length; start++)
        {
            if (live[start] != Empty || visited[start])
            {
                continue;
            }

            List<int> region = [start];
            visited[start] = true;
            bool touchesBlack = false;
            bool touchesWhite = false;
            for (int i = 0; i < region.Count; i++)
            {
                foreach (int neighbour in Neighbours(size, region[i]))
                {
                    if (live[neighbour] == Black)
                    {
                        touchesBlack = true;
                    }
                    else if (live[neighbour] == White)
                    {
                        touchesWhite = true;
                    }
                    else if (!visited[neighbour])
                    {
                        visited[neighbour] = true;
                        region.Add(neighbour);
                    }
                }
            }

            byte regionOwner = touchesBlack == touchesWhite ? Empty : touchesBlack ? Black : White;
            foreach (int point in region)
            {
                owner[point] = regionOwner;
            }
        }

        return owner;
    }

    /// <summary>Area scoring: a side's stones on the board plus the empty points only it surrounds.</summary>
    public static (int Black, int White) AreaScore(byte[] board, int size, bool[]? dead)
    {
        byte[] territory = Territory(board, size, dead);
        int black = 0;
        int white = 0;
        for (int i = 0; i < board.Length; i++)
        {
            byte point = board[i] != Empty && dead?[i] != true ? board[i] : territory[i];
            if (point == Black)
            {
                black++;
            }
            else if (point == White)
            {
                white++;
            }
        }

        return (black, white);
    }
}

/// <summary>
/// Go on a 9x9, 13x13 or 19x19 board with captures, no suicide, the simple ko rule, passing and area (Chinese) scoring with a 7.5
/// komi. Two passes in a row end play; the dead stones are then marked by hand and the score follows. Black moves first, and
/// against the computer you play Black.
/// </summary>
public class GoGame : GameBase
{
    private const double Komi = 7.5;

    private sealed record Snapshot(byte[] Board, byte[]? Previous, bool BlackToMove, int Passes, int BlackCaptures, int WhiteCaptures, int LastMove, string Note);

    private readonly Stack<Snapshot> _history = new();
    private int _size = 9;
    private byte[] _board = new byte[81];
    private byte[]? _previous;
    private bool _blackToMove = true;
    private int _passes;
    private int _blackCaptures;
    private int _whiteCaptures;
    private int _lastMove = -1;
    private string _note = string.Empty;
    private string _message = string.Empty;
    private bool[] _dead = new bool[81];
    private int _cursorX = 4;
    private int _cursorY = 4;

    public GoGame() : this(null)
    {
    }

    internal GoGame(Random? random) : base(random)
    {
    }

    public override string Name => "Go";

    public override string Rules => "Surround points and capture stones with no free neighbour points left. P passes; after two passes mark the dead stones and the score is shown.";

    public override bool IsOver => _passes >= 2;

    public override bool CanUndo => _history.Count > 0;

    public override bool IsComputerTurn => VsComputer && !IsOver && !_blackToMove;

    public override IEnumerable<(string Key, string Description)> ExtraKeys => [("P", "Pass"), ("S", "Change the board size and start again")];

    internal int Size => _size;

    internal byte[] Board => _board;

    internal bool BlackToMove => _blackToMove;

    internal int Passes => _passes;

    internal int BlackCaptures => _blackCaptures;

    internal int WhiteCaptures => _whiteCaptures;

    internal bool[] Dead => _dead;

    internal int CursorIndex
    {
        get => _cursorY * _size + _cursorX;
        set
        {
            _cursorX = value % _size;
            _cursorY = value / _size;
        }
    }

    public override string Status
    {
        get
        {
            if (_message.Length > 0)
            {
                return _message;
            }

            if (IsOver)
            {
                (int black, int white) = GoRules.AreaScore(_board, _size, _dead);
                double whiteTotal = white + Komi;
                string winner = black > whiteTotal ? $"Black wins by {black - whiteTotal:0.#}" : $"White wins by {whiteTotal - black:0.#}";
                return $"Both passed. Select dead stones. Black {black}, White {white} + {Komi} komi: {winner}.";
            }

            string side = _blackToMove ? "Black" : "White";
            string passed = _note.Length > 0 ? $" {_note}" : string.Empty;
            return $"{side} to move. Captured: Black {_blackCaptures}, White {_whiteCaptures}.{passed}";
        }
    }

    public override void NewGame() => NewGame(_size);

    internal void NewGame(int size)
    {
        Version++;
        _size = size;
        _board = new byte[size * size];
        _dead = new bool[size * size];
        _previous = null;
        _blackToMove = true;
        _passes = 0;
        _blackCaptures = 0;
        _whiteCaptures = 0;
        _lastMove = -1;
        _note = string.Empty;
        _message = string.Empty;
        _history.Clear();
        _cursorX = size / 2;
        _cursorY = size / 2;
    }

    public override void MoveCursor(int dx, int dy)
    {
        _cursorX = Clamp(_cursorX + dx, _size - 1);
        _cursorY = Clamp(_cursorY + dy, _size - 1);
    }

    private bool HumanMayAct => !(VsComputer && !_blackToMove);

    public override void Activate()
    {
        _message = string.Empty;
        if (IsOver)
        {
            MarkDeadAt(CursorIndex);
            return;
        }

        if (!HumanMayAct)
        {
            return;
        }

        if (!TryPlay(CursorIndex))
        {
            _message = _board[CursorIndex] != GoRules.Empty ? "That point is taken." : "That move is not allowed: it is suicide or breaks the ko rule.";
        }
    }

    public override bool TryChar(char c)
    {
        switch (char.ToLowerInvariant(c))
        {
            case 'p':
                _message = string.Empty;
                if (!IsOver && HumanMayAct)
                {
                    Pass();
                }

                return true;
            case 's':
                NewGame(_size switch { 9 => 13, 13 => 19, _ => 9 });
                return true;
            default:
                return false;
        }
    }

    private void Push() =>
        _history.Push(new Snapshot(_board, _previous, _blackToMove, _passes, _blackCaptures, _whiteCaptures, _lastMove, _note));

    /// <summary>Plays a stone for the side to move; false if the move is not allowed.</summary>
    internal bool TryPlay(int index)
    {
        byte color = _blackToMove ? GoRules.Black : GoRules.White;
        if (IsOver || !GoRules.TryPlace(_board, _size, color, index, _previous, out byte[] result, out int captured))
        {
            return false;
        }

        Version++;
        Push();
        _previous = _board;
        _board = result;
        if (_blackToMove)
        {
            _blackCaptures += captured;
        }
        else
        {
            _whiteCaptures += captured;
        }

        _blackToMove = !_blackToMove;
        _passes = 0;
        _lastMove = index;
        _note = string.Empty;
        return true;
    }

    internal void Pass()
    {
        if (IsOver)
        {
            return;
        }

        Version++;
        Push();
        _note = $"{(_blackToMove ? "Black" : "White")} passed.";
        _previous = _board;
        _blackToMove = !_blackToMove;
        _passes++;
        _lastMove = -1;
        if (IsOver)
        {
            _dead = new bool[_board.Length];
        }
    }

    /// <summary>After the game, flags (or unflags) the whole group at the point as dead.</summary>
    private void MarkDeadAt(int index)
    {
        if (_board[index] == GoRules.Empty)
        {
            return;
        }

        bool dead = !_dead[index];
        foreach (int stone in GoRules.Group(_board, _size, index).Stones)
        {
            _dead[stone] = dead;
        }
    }

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
        if (VsComputer && !_blackToMove && _history.Count > 0)
        {
            Restore();
        }
    }

    private void Restore()
    {
        Snapshot snapshot = _history.Pop();
        _board = snapshot.Board;
        _previous = snapshot.Previous;
        _blackToMove = snapshot.BlackToMove;
        _passes = snapshot.Passes;
        _blackCaptures = snapshot.BlackCaptures;
        _whiteCaptures = snapshot.WhiteCaptures;
        _lastMove = snapshot.LastMove;
        _note = snapshot.Note;
        _dead = new bool[_board.Length];
    }

    private IEnumerable<int> StarPoints()
    {
        int[] lines = _size switch { 9 => [2, 4, 6], 13 => [3, 6, 9], _ => [3, 9, 15] };
        foreach (int y in lines)
        {
            foreach (int x in lines)
            {
                if (_size != 9 || (x == 4) == (y == 4))
                {
                    yield return y * _size + x;
                }
            }
        }
    }

    public override IReadOnlyList<GameCell[]> Render()
    {
        const string letters = "ABCDEFGHJKLMNOPQRST";
        Color wood = Color.DarkYellow;
        HashSet<int> stars = [.. StarPoints()];
        byte[] territory = IsOver ? GoRules.Territory(_board, _size, _dead) : new byte[_board.Length];
        List<GameCell[]> rows = [[new GameCell("   "), .. Enumerable.Range(0, _size).Select(x => new GameCell($"{letters[x]} ", Color.DarkGray))]];
        for (int y = 0; y < _size; y++)
        {
            List<GameCell> row = [new GameCell($"{_size - y,2} ", Color.DarkGray)];
            for (int x = 0; x < _size; x++)
            {
                int index = y * _size + x;
                byte stone = _board[index];
                string tail = x < _size - 1 ? "─" : " ";
                GameCell cell;
                if (stone != GoRules.Empty)
                {
                    bool dead = _dead[index] && IsOver;
                    Color color = stone == GoRules.Black ? Color.Black : Color.White;
                    cell = new GameCell((dead ? "x" : "●") + tail, dead ? Color.Red : color, index == _lastMove ? Color.DarkRed : wood);
                }
                else if (IsOver && territory[index] != GoRules.Empty)
                {
                    cell = new GameCell("▪" + tail, territory[index] == GoRules.Black ? Color.Black : Color.White, wood);
                }
                else
                {
                    string point = stars.Contains(index) ? "╋" : (y, x) switch
                    {
                        (0, 0) => "┌",
                        (0, _) when x == _size - 1 => "┐",
                        (0, _) => "┬",
                        _ when y == _size - 1 && x == 0 => "└",
                        _ when y == _size - 1 && x == _size - 1 => "┘",
                        _ when y == _size - 1 => "┴",
                        (_, 0) => "├",
                        _ when x == _size - 1 => "┤",
                        _ => "┼",
                    };
                    cell = new GameCell(point + tail, Color.Black, wood);
                }

                row.Add(index == CursorIndex ? cell.WithCursor() : cell);
            }

            rows.Add([.. row]);
        }

        return rows;
    }

    public override Func<CancellationToken, Action?>? PlanComputerMove()
    {
        if (!IsComputerTurn)
        {
            return null;
        }

        byte[] board = _board;
        byte[]? previous = _previous;
        int size = _size;
        int lastMove = _lastMove;
        bool opponentPassed = _passes > 0;
        int moves = _history.Count;
        int version = Version;
        int seed = Random.Next();
        return token =>
        {
            int choice = GoComputer.Choose(board, size, GoRules.White, previous, lastMove, opponentPassed, moves, new Random(seed), token);
            return () =>
            {
                if (version != Version)
                {
                    return;
                }

                if (choice < 0 || !TryPlay(choice))
                {
                    Pass();
                }
            };
        };
    }
}

/// <summary>
/// A modest computer opponent for Go: every legal point is scored by what it captures, saves, threatens and gives away,
/// with a little noise, and it passes when nothing is worth playing.
/// </summary>
internal static class GoComputer
{
    /// <summary>The point to play, or -1 to pass.</summary>
    public static int Choose(byte[] board, int size, byte color, byte[]? previous, int lastMove, bool opponentPassed, int moveCount, Random random, CancellationToken token)
    {
        byte opponent = GoRules.Other(color);
        byte[] territory = GoRules.Territory(board, size, null);
        bool[] hasStone = new bool[board.Length];
        for (int i = 0; i < board.Length; i++)
        {
            hasStone[i] = board[i] != GoRules.Empty;
        }

        int best = -1;
        double bestScore = double.MinValue;
        for (int index = 0; index < board.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            if (board[index] != GoRules.Empty || !GoRules.TryPlace(board, size, color, index, previous, out byte[] result, out int captured))
            {
                continue;
            }

            double score = Score(board, result, size, color, opponent, index, captured, territory, lastMove, moveCount, hasStone) + random.NextDouble() * 1.5;
            if (score > bestScore)
            {
                bestScore = score;
                best = index;
            }
        }

        if (best < 0)
        {
            return -1;
        }

        (int black, int white) = GoRules.AreaScore(board, size, null);
        double ahead = color == GoRules.White ? white + 7.5 - black : black - white - 7.5;
        if (opponentPassed && (ahead >= 0 || bestScore < 10))
        {
            return -1;
        }

        return bestScore < 0.5 || moveCount > size * size * 3 ? -1 : best;
    }

    private static double Score(byte[] board, byte[] result, int size, byte color, byte opponent, int index, int captured, byte[] territory, int lastMove, int moveCount, bool[] hasStone)
    {
        double score = captured * 12;
        (List<int> stones, HashSet<int> liberties) = GoRules.Group(result, size, index);
        score += Math.Min(liberties.Count, 4);
        if (liberties.Count == 1 && captured == 0)
        {
            score -= 15 + stones.Count * 3;
        }

        bool allOwn = true;
        foreach (int neighbour in GoRules.Neighbours(size, index))
        {
            if (board[neighbour] == color)
            {
                (List<int> group, HashSet<int> before) = GoRules.Group(board, size, neighbour);
                if (before.Count == 1 && liberties.Count > 1)
                {
                    score += 10 + group.Count;
                }
            }
            else if (board[neighbour] == opponent)
            {
                allOwn = false;
                (List<int> group, HashSet<int> after) = GoRules.Group(result, size, neighbour);
                if (after.Count == 1)
                {
                    score += 6 + group.Count;
                }
                else if (after.Count == 2)
                {
                    score += 1.5;
                }
            }
            else
            {
                allOwn = false;
            }
        }

        if (allOwn && captured == 0)
        {
            score -= 25;
        }

        if (territory[index] == color)
        {
            score -= 30;
        }

        int x = index % size;
        int y = index / size;
        int line = Math.Min(Math.Min(x, y), Math.Min(size - 1 - x, size - 1 - y));
        score += line switch { 0 => -4, 1 => -0.5, 2 => 2, 3 => 1.5, _ => 0 } * (moveCount < size * 2 ? 1.5 : 0.5);
        if (lastMove >= 0 && Math.Abs(x - lastMove % size) <= 2 && Math.Abs(y - lastMove / size) <= 2)
        {
            score += 3;
        }

        return score;
    }
}

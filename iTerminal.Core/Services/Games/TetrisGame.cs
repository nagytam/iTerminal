using iConsole.Display;

namespace iTerminal.Core.Services.Games;

/// <summary>
/// Tetris on a 10 x 20 board, for one person. The pieces fall on the clock the dialog passes to <see cref="Tick"/>; the
/// left and right arrows slide the piece, the down arrow drops it one row faster, the up arrow (or X) turns it clockwise,
/// Z turns it back and D drops it at once. A full row is cleared; every ten rows raise the level and the fall speeds up.
/// The pieces come from a shuffled bag of all seven, so a long wait for one shape cannot happen. Enter pauses and
/// resumes, and leaving the game pauses it.
/// </summary>
public class TetrisGame : GameBase
{
    internal const int Columns = 10;
    internal const int Rows = 20;

    private const int NoPiece = -1;

    // Each piece as the cells of its first turn in a square box; turning rotates the box, so the I and O pieces need no special case.
    private static readonly (int Size, (int X, int Y)[] Cells, Color Color)[] Pieces =
    [
        (4, [(0, 1), (1, 1), (2, 1), (3, 1)], Color.Cyan),         // I
        (2, [(0, 0), (1, 0), (0, 1), (1, 1)], Color.Yellow),       // O
        (3, [(1, 0), (0, 1), (1, 1), (2, 1)], Color.Magenta),      // T
        (3, [(1, 0), (2, 0), (0, 1), (1, 1)], Color.Green),        // S
        (3, [(0, 0), (1, 0), (1, 1), (2, 1)], Color.Red),          // Z
        (3, [(0, 0), (0, 1), (1, 1), (2, 1)], Color.Blue),         // J
        (3, [(2, 0), (0, 1), (1, 1), (2, 1)], Color.DarkYellow),   // L
    ];

    private static readonly string[] PieceNames = ["I", "O", "T", "S", "Z", "J", "L"];

    private readonly int[,] _board = new int[Rows, Columns];
    private readonly Queue<int> _bag = new();
    private int _piece = NoPiece;
    private int _next;
    private (int X, int Y)[] _cells = [];
    private int _size;
    private int _left;
    private int _top;
    private TimeSpan _accumulator;
    private TetrisState _state;

    public TetrisGame() : this(null)
    {
    }

    internal TetrisGame(Random? random) : base(random)
    {
        NewGame();
    }

    private enum TetrisState
    {
        Stopped,
        Falling,
        Paused,
        Over,
    }

    public override string Name => "Tetris";

    public override string Rules => "Stack the falling pieces into full rows. A full row disappears. The game ends when the pieces reach the top.";

    public override string Status => _state switch
    {
        TetrisState.Stopped => "Press an arrow key to start.",
        TetrisState.Paused => "Paused. Enter to go on.",
        TetrisState.Over => $"Game over with {Score} points and {Lines} rows. N to play again.",
        _ => $"Score {Score}  Rows {Lines}  Level {Level}  Next {PieceNames[_next]}",
    };

    public override bool IsOver => _state == TetrisState.Over;

    public override bool CanUndo => false;

    public override bool IsComputerTurn => false;

    public override bool OffersComputerOpponent => false;

    public override bool OffersUndo => false;

    public override string ActivateLabel => "pause";

    public override string ActivateHint => "Pause or resume the game, or start the pieces falling";

    public override IEnumerable<(string Key, string Description)> ExtraKeys =>
    [
        ("Up / X", "Turn the piece clockwise"),
        ("Z", "Turn the piece counter-clockwise"),
        ("D", "Drop the piece at once"),
    ];

    public int Score { get; private set; }

    public int Lines { get; private set; }

    public int Level => Lines / 10 + 1;

    internal bool IsFalling => _state == TetrisState.Falling;

    internal bool IsPaused => _state == TetrisState.Paused;

    /// <summary>The time one row of fall takes: 800 ms at level 1, 70 ms less for each level, never under 80 ms.</summary>
    internal TimeSpan FallInterval => TimeSpan.FromMilliseconds(Math.Max(80, 800 - 70 * (Level - 1)));

    internal int PieceLeft => _left;

    internal int PieceTop => _top;

    internal int CurrentPiece => _piece;

    internal int NextPiece => _next;

    /// <summary>The cells the falling piece covers, as board coordinates.</summary>
    internal IEnumerable<(int X, int Y)> PieceCells => _cells.Select(c => (_left + c.X, _top + c.Y));

    internal bool IsFilled(int x, int y) => _board[y, x] != 0;

    /// <summary>Fills a board cell for a test.</summary>
    internal void FillForTest(int x, int y, int piece = 0) => _board[y, x] = piece + 1;

    /// <summary>Puts a given piece at the top for a test, and starts the fall.</summary>
    internal void SpawnForTest(int piece)
    {
        _next = piece;
        Spawn();
        if (_state != TetrisState.Over)
        {
            _state = TetrisState.Falling;
        }
    }

    public override void NewGame()
    {
        Array.Clear(_board);
        _bag.Clear();
        Score = 0;
        Lines = 0;
        _accumulator = TimeSpan.Zero;
        _state = TetrisState.Stopped;
        _next = DrawFromBag();
        Spawn();
    }

    public override void MoveCursor(int dx, int dy)
    {
        if (_state == TetrisState.Over || _state == TetrisState.Paused)
        {
            return;
        }

        if (_state == TetrisState.Stopped)
        {
            _state = TetrisState.Falling;
            _accumulator = TimeSpan.Zero;
        }

        if (dx != 0)
        {
            TryMove(Math.Sign(dx), 0);
        }
        else if (dy > 0)
        {
            // A soft drop pays one point a row; at the bottom it locks the piece.
            if (TryMove(0, 1))
            {
                Score++;
            }
            else
            {
                Lock();
            }

            _accumulator = TimeSpan.Zero;
        }
        else if (dy < 0)
        {
            Rotate(clockwise: true);
        }
    }

    public override void Activate()
    {
        switch (_state)
        {
            case TetrisState.Stopped:
                _state = TetrisState.Falling;
                _accumulator = TimeSpan.Zero;
                break;
            case TetrisState.Falling:
                Pause();
                break;
            case TetrisState.Paused:
                _state = TetrisState.Falling;
                break;
        }
    }

    public override bool TryChar(char c)
    {
        if (_state != TetrisState.Falling && _state != TetrisState.Stopped)
        {
            return false;
        }

        switch (char.ToLowerInvariant(c))
        {
            case 'x':
                StartIfStopped();
                Rotate(clockwise: true);
                return true;
            case 'z':
                StartIfStopped();
                Rotate(clockwise: false);
                return true;
            case 'd':
                StartIfStopped();
                HardDrop();
                return true;
            default:
                return false;
        }
    }

    public override void Pause()
    {
        if (_state != TetrisState.Falling)
        {
            return;
        }

        _state = TetrisState.Paused;
        _accumulator = TimeSpan.Zero;
    }

    public override void Undo()
    {
    }

    public override bool Tick(TimeSpan elapsed)
    {
        if (_state != TetrisState.Falling)
        {
            return false;
        }

        if (elapsed > TimeSpan.Zero)
        {
            _accumulator += elapsed;
        }

        // The remainder carries over, so a steady stream of short ticks drops the piece as often as one long one.
        bool moved = false;
        while (_state == TetrisState.Falling && _accumulator >= FallInterval)
        {
            _accumulator -= FallInterval;
            if (!TryMove(0, 1))
            {
                Lock();
            }

            moved = true;
        }

        return moved;
    }

    public override IReadOnlyList<GameCell[]> Render()
    {
        HashSet<(int X, int Y)> falling = _state == TetrisState.Over ? [] : [.. PieceCells];
        List<GameCell[]> rows = [];
        for (int y = 0; y < Rows; y++)
        {
            GameCell[] row = new GameCell[Columns + 2];
            row[0] = new GameCell("│", Color.DarkGray);
            for (int x = 0; x < Columns; x++)
            {
                row[x + 1] = falling.Contains((x, y))
                    ? new GameCell("  ", null, Pieces[_piece].Color)
                    : _board[y, x] != 0
                        ? new GameCell("  ", null, Pieces[_board[y, x] - 1].Color)
                        : new GameCell(" ·", Color.DarkGray);
            }

            row[Columns + 1] = new GameCell("│", Color.DarkGray);
            rows.Add(row);
        }

        return rows;
    }

    public override Func<CancellationToken, Action?>? PlanComputerMove() => null;

    private void StartIfStopped()
    {
        if (_state == TetrisState.Stopped)
        {
            _state = TetrisState.Falling;
            _accumulator = TimeSpan.Zero;
        }
    }

    private int DrawFromBag()
    {
        if (_bag.Count == 0)
        {
            int[] order = [.. Enumerable.Range(0, Pieces.Length)];
            Random.Shuffle(order);
            foreach (int piece in order)
            {
                _bag.Enqueue(piece);
            }
        }

        return _bag.Dequeue();
    }

    /// <summary>Brings the next piece in at the top. If it has no room, the game is over.</summary>
    private void Spawn()
    {
        _piece = _next;
        _next = DrawFromBag();
        _cells = [.. Pieces[_piece].Cells];
        _size = Pieces[_piece].Size;
        _left = (Columns - _size) / 2;
        _top = 0;
        if (Collides(_cells, _left, _top))
        {
            _state = TetrisState.Over;
        }
    }

    private bool Collides((int X, int Y)[] cells, int left, int top)
    {
        foreach ((int X, int Y) cell in cells)
        {
            int x = left + cell.X;
            int y = top + cell.Y;
            if (x < 0 || x >= Columns || y >= Rows || (y >= 0 && _board[y, x] != 0))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryMove(int dx, int dy)
    {
        if (Collides(_cells, _left + dx, _top + dy))
        {
            return false;
        }

        _left += dx;
        _top += dy;
        return true;
    }

    private void Rotate(bool clockwise)
    {
        (int X, int Y)[] turned = _cells
            .Select(c => clockwise ? (X: _size - 1 - c.Y, Y: c.X) : (X: c.Y, Y: _size - 1 - c.X))
            .ToArray();

        // A turn next to a wall or a stack may push the piece aside by a cell or two to fit.
        foreach (int kick in new[] { 0, -1, 1, -2, 2 })
        {
            if (!Collides(turned, _left + kick, _top))
            {
                _cells = turned;
                _left += kick;
                return;
            }
        }
    }

    private void HardDrop()
    {
        int rows = 0;
        while (TryMove(0, 1))
        {
            rows++;
        }

        Score += 2 * rows;
        Lock();
        _accumulator = TimeSpan.Zero;
    }

    /// <summary>The piece comes to rest: it joins the stack, full rows go, and the next piece enters.</summary>
    private void Lock()
    {
        foreach ((int X, int Y) cell in PieceCells)
        {
            if (cell.Y >= 0)
            {
                _board[cell.Y, cell.X] = _piece + 1;
            }
        }

        int cleared = ClearRows();
        if (cleared > 0)
        {
            Score += cleared switch { 1 => 100, 2 => 300, 3 => 500, _ => 800 } * Level;
            Lines += cleared;
        }

        Spawn();
    }

    private int ClearRows()
    {
        int cleared = 0;
        for (int y = Rows - 1; y >= 0; y--)
        {
            bool full = true;
            for (int x = 0; x < Columns && full; x++)
            {
                full = _board[y, x] != 0;
            }

            if (!full)
            {
                continue;
            }

            for (int row = y; row > 0; row--)
            {
                for (int x = 0; x < Columns; x++)
                {
                    _board[row, x] = _board[row - 1, x];
                }
            }

            for (int x = 0; x < Columns; x++)
            {
                _board[0, x] = 0;
            }

            cleared++;
            y++; // the rows moved down: look at this one again
        }

        return cleared;
    }
}

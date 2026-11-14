using iConsole.Display;
using iConsole.Localization;

namespace iTerminal.Core.Services.Games;

/// <summary>
/// Tetris on a 10 x 20 board. The pieces fall on the clock the dialog passes to <see cref="Tick"/>; the
/// left and right arrows slide the piece, the down arrow drops it one row faster, the up arrow (or X) turns it clockwise,
/// Z turns it back and D drops it at once. A full row is cleared; every ten rows raise the level and the fall speeds up.
/// The pieces come from a shuffled bag of all seven, so a long wait for one shape cannot happen. Enter pauses and
/// resumes, and leaving the game pauses it.
///
/// <para>Against the computer a second board runs beside the first on the same clock. Its pieces are placed by a planner that
/// scores every turn and column with the usual stack measures (height, holes, bumpiness, rows cleared), with a little noise so
/// it can be beaten, and it walks each piece there one action at a time. The first stack to reach the top loses.</para>
/// </summary>
public class TetrisGame : GameBase
{
    internal const int Columns = 10;
    internal const int Rows = 20;

    private const int NoPiece = -1;
    private static readonly TimeSpan RivalActionInterval = TimeSpan.FromMilliseconds(100);

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
    private readonly bool _isRival;
    private readonly TetrisGame? _rival;
    private int _piece = NoPiece;
    private int _next;
    private (int X, int Y)[] _cells = [];
    private int _size;
    private int _left;
    private int _top;
    private TimeSpan _accumulator;
    private TimeSpan _actionClock;
    private TetrisState _state;
    private bool _topped;
    private int _plannedRotations;
    private int _targetLeft;

    public TetrisGame() : this(null)
    {
    }

    internal TetrisGame(Random? random) : this(random, isRival: false)
    {
    }

    private TetrisGame(Random? random, bool isRival) : base(random)
    {
        _isRival = isRival;
        if (!isRival)
        {
            _rival = new TetrisGame(new Random(Random.Next()), isRival: true);
        }

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

    public override string Rules => VsComputer && _rival is not null
        ? GameText.Get("Games.Tetris.RulesVersus", "Stack the falling pieces into full rows. The computer's board runs beside yours: whoever's stack reaches the top first loses.")
        : GameText.Get("Games.Tetris.Rules", "Stack the falling pieces into full rows. A full row disappears. The game ends when the pieces reach the top.");

    public override string Status => _state switch
    {
        TetrisState.Stopped => GameText.Get("Games.PressArrow", "Press an arrow key to start."),
        TetrisState.Paused => GameText.Get("Games.Paused", "Paused. Enter to go on."),
        TetrisState.Over => OverText(),
        _ when VsComputer && _rival is not null => string.Format(
            GameText.Get("Games.Tetris.VersusStatus", "Score {0}  Rows {1}  Level {2}  Next {3}  |  Computer {4} points, {5} rows"),
            Score, Lines, Level, PieceNames[_next], _rival.Score, _rival.Lines),
        _ => string.Format(GameText.Get("Games.Tetris.Status", "Score {0}  Rows {1}  Level {2}  Next {3}"), Score, Lines, Level, PieceNames[_next]),
    };

    public override bool IsOver => _state == TetrisState.Over;

    public override bool CanUndo => false;

    public override bool IsComputerTurn => false;

    public override bool OffersUndo => false;

    public override string? ModeLabel => VsComputer
        ? GameText.Get("Games.Tetris.VsComputer", "against the computer")
        : UiText.Get("Games.OnePlayer", "one player");

    public override string ActivateLabel => iConsole.Localization.UiText.Get("Games.Snake.Pause", "pause");

    public override string ActivateHint => GameText.Get("Games.Tetris.ActivateHint", "Pause or resume the game, or start the pieces falling");

    public override IEnumerable<(string Key, string Description)> ExtraKeys =>
    [
        ("Up / X", GameText.Get("Games.Tetris.Turn", "Turn the piece clockwise")),
        ("Z", GameText.Get("Games.Tetris.TurnBack", "Turn the piece counter-clockwise")),
        ("D", GameText.Get("Games.Tetris.Drop", "Drop the piece at once")),
    ];

    public int Score { get; private set; }

    public int Lines { get; private set; }

    /// <summary>How many pieces have come to rest on this board; with the rows cleared it accounts for every filled cell.</summary>
    internal int PiecesLocked { get; private set; }

    public int Level => Lines / 10 + 1;

    /// <summary>The computer's board, when the computer opponent is on; null on the rival itself.</summary>
    internal TetrisGame? Rival => _rival;

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
        PiecesLocked = 0;
        _accumulator = TimeSpan.Zero;
        _actionClock = TimeSpan.Zero;
        _topped = false;
        // The computer's board has no start key: it is already falling when the person starts theirs, and it waits for the clock.
        _state = _isRival ? TetrisState.Falling : TetrisState.Stopped;
        _next = DrawFromBag();
        Spawn();
        _rival?.NewGame();
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

        bool moved = Fall(elapsed);
        if (VsComputer && _rival is not null && _state == TetrisState.Falling)
        {
            moved |= _rival.AdvanceRival(elapsed);
            // The computer's stack reaching the top ends the match for the person too.
            if (_rival._topped)
            {
                _state = TetrisState.Over;
            }
        }

        return moved;
    }

    /// <summary>The piece falls on the clock; the remainder carries over, so a steady stream of short ticks drops it as often as one long one.</summary>
    private bool Fall(TimeSpan elapsed)
    {
        if (elapsed > TimeSpan.Zero)
        {
            _accumulator += elapsed;
        }

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

    /// <summary>
    /// Plays the computer's board on its own clock: one action (a turn or a slide towards the planned column) every 100 ms,
    /// and the piece falls at the level's speed. Returns true when it acted.
    /// </summary>
    internal bool AdvanceRival(TimeSpan elapsed)
    {
        if (!_isRival || _topped || elapsed <= TimeSpan.Zero)
        {
            return false;
        }

        _actionClock += elapsed;
        bool acted = false;
        while (_actionClock >= RivalActionInterval && !_topped)
        {
            _actionClock -= RivalActionInterval;
            if (_plannedRotations > 0)
            {
                _plannedRotations--;
                Rotate(clockwise: true);
            }
            else if (_left != _targetLeft && !TryMove(Math.Sign(_targetLeft - _left), 0))
            {
                _targetLeft = _left;
            }

            _accumulator += RivalActionInterval;
            while (!_topped && _accumulator >= FallInterval)
            {
                _accumulator -= FallInterval;
                if (!TryMove(0, 1))
                {
                    Lock();
                    break;
                }
            }

            acted = true;
        }

        return acted;
    }

    /// <summary>
    /// The board, two rows of cells to a text row: half-block characters show the upper cell in the foreground and the lower
    /// one in the background, so the 20-row well takes 10 rows and fits a 24-row terminal.
    /// </summary>
    public override IReadOnlyList<GameCell[]> Render()
    {
        List<GameCell[]> own = RenderBoard();
        if (!VsComputer || _rival is null)
        {
            return own;
        }

        List<GameCell[]> rival = _rival.RenderBoard();
        int width = (Columns + 2) * 2 - 2;
        List<GameCell[]> rows =
        [
            [new GameCell(GameText.Get("Games.Tetris.You", "You").PadRight(width), Color.Cyan), new GameCell("   "), new GameCell(GameText.Get("Games.Tetris.Computer", "Computer"), Color.Yellow)],
        ];
        for (int r = 0; r < own.Count; r++)
        {
            rows.Add([.. own[r], new GameCell("   "), .. rival[r]]);
        }

        return rows;
    }

    private List<GameCell[]> RenderBoard()
    {
        HashSet<(int X, int Y)> falling = _state == TetrisState.Over ? [] : [.. PieceCells];
        List<GameCell[]> rows = [];
        for (int y = 0; y < Rows; y += 2)
        {
            GameCell[] row = new GameCell[Columns + 2];
            row[0] = new GameCell("│", Color.DarkGray);
            for (int x = 0; x < Columns; x++)
            {
                Color? upper = CellColour(falling, x, y);
                Color? lower = CellColour(falling, x, y + 1);
                row[x + 1] = (upper, lower) switch
                {
                    (null, null) => new GameCell(" ·", Color.DarkGray),
                    (Color top, null) => new GameCell("▀▀", top),
                    (null, Color bottom) => new GameCell("▄▄", bottom),
                    (Color top, Color bottom) when top == bottom => new GameCell("  ", null, top),
                    (Color top, Color bottom) => new GameCell("▀▀", top, bottom),
                };
            }

            row[Columns + 1] = new GameCell("│", Color.DarkGray);
            rows.Add(row);
        }

        GameCell[] floor = new GameCell[Columns + 2];
        floor[0] = new GameCell("└", Color.DarkGray);
        for (int x = 0; x < Columns; x++)
        {
            floor[x + 1] = new GameCell("──", Color.DarkGray);
        }

        floor[Columns + 1] = new GameCell("┘", Color.DarkGray);
        rows.Add(floor);
        return rows;
    }

    private Color? CellColour(HashSet<(int X, int Y)> falling, int x, int y)
    {
        if (falling.Contains((x, y)))
        {
            return Pieces[_piece].Color;
        }

        return _board[y, x] != 0 ? Pieces[_board[y, x] - 1].Color : null;
    }

    private string OverText()
    {
        if (VsComputer && _rival is not null)
        {
            return (_topped, _rival._topped) switch
            {
                (true, true) => string.Format(GameText.Get("Games.Tetris.VersusDraw", "Both stacks reached the top: {0} points to the computer's {1}. N to play again."), Score, _rival.Score),
                (true, false) => string.Format(GameText.Get("Games.Tetris.VersusLost", "The computer wins: {0} points to your {1}. N to play again."), _rival.Score, Score),
                _ => string.Format(GameText.Get("Games.Tetris.VersusWon", "You win: {0} points to the computer's {1}. N to play again."), Score, _rival.Score),
            };
        }

        return string.Format(GameText.Get("Games.Tetris.Over", "Game over with {0} points and {1} rows. N to play again."), Score, Lines);
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
            _topped = true;
            _state = TetrisState.Over;
            return;
        }

        if (_isRival)
        {
            PlanRival();
        }
    }

    /// <summary>
    /// Finds the turn and column that leave the best board after the piece drops, as the planner's score sees it. Every turn
    /// is tried from the piece's spawn orientation, and every column it fits in.
    /// </summary>
    private void PlanRival()
    {
        double bestScore = double.MinValue;
        int bestRotation = 0;
        int bestLeft = _left;
        (int X, int Y)[] cells = _cells;
        for (int rotation = 0; rotation < 4; rotation++)
        {
            for (int left = -2; left <= Columns; left++)
            {
                if (Collides(cells, left, 0))
                {
                    continue;
                }

                int top = 0;
                while (!Collides(cells, left, top + 1))
                {
                    top++;
                }

                double score = Evaluate(cells, left, top) + Random.NextDouble() * 0.2;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestRotation = rotation;
                    bestLeft = left;
                }
            }

            cells = TurnClockwise(cells, _size);
        }

        _plannedRotations = bestRotation;
        _targetLeft = bestLeft;
    }

    /// <summary>The stack measures after the piece lands: rows it completes, height, holes and bumpiness, weighted as the usual Tetris planners do.</summary>
    private double Evaluate((int X, int Y)[] cells, int left, int top)
    {
        int[,] grid = (int[,])_board.Clone();
        foreach ((int X, int Y) cell in cells)
        {
            grid[top + cell.Y, left + cell.X] = 1;
        }

        int completed = 0;
        for (int y = 0; y < Rows; y++)
        {
            bool full = true;
            for (int x = 0; x < Columns && full; x++)
            {
                full = grid[y, x] != 0;
            }

            completed += full ? 1 : 0;
        }

        int[] heights = new int[Columns];
        int holes = 0;
        for (int x = 0; x < Columns; x++)
        {
            int highest = Array.FindIndex(Enumerable.Range(0, Rows).Select(y => grid[y, x]).ToArray(), v => v != 0);
            if (highest < 0)
            {
                continue;
            }

            heights[x] = Rows - highest;
            for (int y = highest + 1; y < Rows; y++)
            {
                holes += grid[y, x] == 0 ? 1 : 0;
            }
        }

        int aggregate = heights.Sum();
        int bumpiness = 0;
        for (int x = 0; x + 1 < Columns; x++)
        {
            bumpiness += Math.Abs(heights[x] - heights[x + 1]);
        }

        return -0.51 * aggregate + 0.76 * completed - 0.36 * holes - 0.18 * bumpiness;
    }

    private static (int X, int Y)[] TurnClockwise((int X, int Y)[] cells, int size) =>
        cells.Select(c => (X: size - 1 - c.Y, Y: c.X)).ToArray();

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
        PiecesLocked++;
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

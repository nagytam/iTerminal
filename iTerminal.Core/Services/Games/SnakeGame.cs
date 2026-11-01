using iConsole.Display;
using iConsole.Localization;

namespace iTerminal.Core.Services.Games;

/// <summary>
/// Snake on a 20 x 12 board, for one person. The snake starts stopped; an arrow key or Enter sets it moving, and it moves
/// one cell per 120 ms of the time the dialog passes to <see cref="Tick"/>. Every bite makes it one cell longer. A wall
/// or its own body ends the game. Enter pauses and resumes it, and leaving the game pauses it.
/// </summary>
public class SnakeGame : GameBase
{
    private const int Columns = 20;
    private const int Rows = 12;
    private const int StartLength = 3;
    private static readonly TimeSpan StepInterval = TimeSpan.FromMilliseconds(120);

    private readonly List<(int X, int Y)> _body = [];
    private (int X, int Y) _heading = (1, 0);
    private (int X, int Y) _queued = (1, 0);
    private (int X, int Y) _food;
    private TimeSpan _accumulator;
    private SnakeState _state;

    public SnakeGame() : this(null)
    {
    }

    internal SnakeGame(Random? random) : base(random)
    {
        NewGame();
    }

    private enum SnakeState
    {
        Stopped,
        Moving,
        Paused,
        Over,
    }

    public override string Name => "Snake";

    public override string Rules => "Steer the snake to the food. It grows with every bite. Hitting a wall or yourself ends the game.";

    public override string Status => _state switch
    {
        SnakeState.Stopped => "Press an arrow key to start.",
        SnakeState.Paused => "Paused. Enter to go on.",
        SnakeState.Over => $"Game over with {Score} points. N to play again.",
        _ => $"Score {Score}.",
    };

    public override bool IsOver => _state == SnakeState.Over;

    public override bool CanUndo => false;

    public override bool IsComputerTurn => false;

    public override bool OffersComputerOpponent => false;

    public override bool OffersUndo => false;

    public override string ActivateLabel => UiText.Get("Games.Snake.Pause", "pause");

    public override string ActivateHint => UiText.Get("DialogHint.PauseOrResumeSnake","Pause or resume the snake, or start it moving");

    private int Score => _body.Count - StartLength;

    internal (int X, int Y) Head => _body[0];

    internal IReadOnlyList<(int X, int Y)> Body => _body;

    internal (int X, int Y) Food => _food;

    /// <summary>Lets a test put the food where it needs it; the game places it itself otherwise.</summary>
    internal void PutFoodForTest((int X, int Y) cell) => _food = cell;

    public override void NewGame()
    {
        // The head first, the body trailing to its left: the snake faces right, in the left-middle of the board.
        _body.Clear();
        for (int i = 0; i < StartLength; i++)
        {
            _body.Add((5 - i, Rows / 2));
        }

        _heading = (1, 0);
        _queued = (1, 0);
        _accumulator = TimeSpan.Zero;
        _state = SnakeState.Stopped;
        PlaceFood();
    }

    public override void MoveCursor(int dx, int dy)
    {
        if (_state == SnakeState.Over || (dx == 0 && dy == 0))
        {
            return;
        }

        (int X, int Y) direction = (Math.Sign(dx), Math.Sign(dy));
        // A turn straight back onto the body is ignored, and it does not start a stopped snake either. It is checked
        // against the heading of the last step, so two keys pressed within one step cannot turn the snake back into its neck.
        if (direction == (-_heading.X, -_heading.Y))
        {
            return;
        }

        _queued = direction;
        if (_state == SnakeState.Stopped)
        {
            StartMoving();
        }
    }

    public override void Activate()
    {
        switch (_state)
        {
            case SnakeState.Stopped:
                StartMoving();
                break;
            case SnakeState.Moving:
                Pause();
                break;
            case SnakeState.Paused:
                _state = SnakeState.Moving;
                break;
        }
    }

    public override void Pause()
    {
        if (_state != SnakeState.Moving)
        {
            return;
        }

        _state = SnakeState.Paused;
        _accumulator = TimeSpan.Zero;
    }

    public override void Undo()
    {
    }

    public override bool Tick(TimeSpan elapsed)
    {
        if (_state != SnakeState.Moving)
        {
            return false;
        }

        if (elapsed > TimeSpan.Zero)
        {
            _accumulator += elapsed;
        }

        // The remainder carries over, so a steady stream of short ticks moves the snake as often as one long one.
        bool moved = false;
        while (_accumulator >= StepInterval && _state == SnakeState.Moving)
        {
            _accumulator -= StepInterval;
            Step();
            moved = true;
        }

        return moved;
    }

    public override IReadOnlyList<GameCell[]> Render()
    {
        List<GameCell[]> rows = [];
        for (int y = 0; y < Rows; y++)
        {
            GameCell[] row = new GameCell[Columns];
            for (int x = 0; x < Columns; x++)
            {
                row[x] = CellAt((x, y));
            }

            rows.Add(row);
        }

        return rows;
    }

    public override Func<CancellationToken, Action?>? PlanComputerMove() => null;

    private void StartMoving()
    {
        _state = SnakeState.Moving;
        _accumulator = TimeSpan.Zero;
    }

    private void Step()
    {
        _heading = _queued;
        (int X, int Y) head = (_body[0].X + _heading.X, _body[0].Y + _heading.Y);
        bool eats = head == _food;
        if (head.X < 0 || head.X >= Columns || head.Y < 0 || head.Y >= Rows || HitsBody(head, eats))
        {
            _state = SnakeState.Over;
            return;
        }

        _body.Insert(0, head);
        if (eats)
        {
            PlaceFood();
        }
        else
        {
            _body.RemoveAt(_body.Count - 1);
        }
    }

    /// <summary>The tail leaves its cell in the same step, so the head may enter it; a bite keeps the tail where it is.</summary>
    private bool HitsBody((int X, int Y) head, bool eats) =>
        _body.Take(eats ? _body.Count : _body.Count - 1).Contains(head);

    private void PlaceFood()
    {
        List<(int X, int Y)> free = [];
        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                if (!_body.Contains((x, y)))
                {
                    free.Add((x, y));
                }
            }
        }

        _food = free.Count == 0 ? (-1, -1) : free[Random.Next(free.Count)];
    }

    private GameCell CellAt((int X, int Y) cell)
    {
        if (cell == _body[0])
        {
            return new GameCell("  ", null, Color.Yellow);
        }

        if (_body.Contains(cell))
        {
            return new GameCell("  ", null, Color.Green);
        }

        if (cell == _food)
        {
            return new GameCell("  ", null, Color.Red);
        }

        return new GameCell(" ·", Color.DarkGray);
    }
}

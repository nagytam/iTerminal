using iConsole.Display;
using iConsole.Localization;

namespace iTerminal.Core.Services.Games;

/// <summary>
/// Snake on a 20 x 12 board. The snake starts stopped; an arrow key or Enter sets it moving, and it moves one cell per 120 ms
/// of the time the dialog passes to <see cref="Tick"/>. Every bite makes it one cell longer. A wall or a body ends the game.
/// Enter pauses and resumes it, and leaving the game pauses it.
///
/// <para>Against the computer a rival snake races for the same food. It picks each turn by how close it brings it to the food
/// and how much room it keeps, with a little noise so it can be beaten. Whoever crashes first loses; if both crash on the same
/// step it is a draw. The rival does not move when the computer opponent is off.</para>
/// </summary>
public class SnakeGame : GameBase
{
    private const int Columns = 20;
    private const int Rows = 12;
    private const int StartLength = 3;
    private const int RoomCap = 40;
    private static readonly TimeSpan StepInterval = TimeSpan.FromMilliseconds(120);
    private static readonly (int X, int Y)[] Directions = [(1, 0), (0, 1), (-1, 0), (0, -1)];

    private readonly List<(int X, int Y)> _body = [];
    private readonly List<(int X, int Y)> _rival = [];
    private (int X, int Y) _heading = (1, 0);
    private (int X, int Y) _queued = (1, 0);
    private (int X, int Y) _rivalHeading = (-1, 0);
    private (int X, int Y) _food;
    private TimeSpan _accumulator;
    private SnakeState _state;
    private SnakeOutcome _outcome;

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

    private enum SnakeOutcome
    {
        None,
        Crashed,
        Won,
        Lost,
        Draw,
    }

    public override string Name => "Snake";

    public override string Rules => VsComputer
        ? GameText.Get("Games.Snake.RulesVersus", "Race the computer's snake to the food. It grows with every bite. A wall or a snake's body ends that snake's run; if both crash together it is a draw.")
        : GameText.Get("Games.Snake.Rules", "Steer the snake to the food. It grows with every bite. Hitting a wall or yourself ends the game.");

    public override string Status => _state switch
    {
        SnakeState.Stopped => GameText.Get("Games.PressArrow", "Press an arrow key to start."),
        SnakeState.Paused => GameText.Get("Games.Paused", "Paused. Enter to go on."),
        SnakeState.Over => OverText(),
        _ when VsComputer && _rival.Count > 0 => string.Format(GameText.Get("Games.Snake.VersusScore", "Score {0}  Computer {1}."), Score, RivalScore),
        _ => string.Format(GameText.Get("Games.Snake.Score", "Score {0}."), Score),
    };

    public override bool IsOver => _state == SnakeState.Over;

    public override bool CanUndo => false;

    public override bool IsComputerTurn => false;

    public override bool OffersUndo => false;

    public override string? ModeLabel => VsComputer
        ? GameText.Get("Games.Snake.VsComputer", "against the computer")
        : UiText.Get("Games.OnePlayer", "one player");

    public override string ActivateLabel => UiText.Get("Games.Snake.Pause", "pause");

    public override string ActivateHint => UiText.Get("DialogHint.PauseOrResumeSnake", "Pause or resume the snake, or start it moving");

    private int Score => _body.Count - StartLength;

    private int RivalScore => _rival.Count - StartLength;

    internal (int X, int Y) Head => _body[0];

    internal IReadOnlyList<(int X, int Y)> Body => _body;

    /// <summary>The rival snake, empty when the computer opponent is off.</summary>
    internal IReadOnlyList<(int X, int Y)> Rival => _rival;

    internal (int X, int Y) Food => _food;

    /// <summary>Lets a test put the food where it needs it; the game places it itself otherwise.</summary>
    internal void PutFoodForTest((int X, int Y) cell) => _food = cell;

    /// <summary>Lets a test put the rival where it needs it, with its heading.</summary>
    internal void PutRivalForTest(IEnumerable<(int X, int Y)> body, (int X, int Y) heading)
    {
        _rival.Clear();
        _rival.AddRange(body);
        _rivalHeading = heading;
    }

    public override void NewGame()
    {
        Version++;
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
        _outcome = SnakeOutcome.None;
        _rival.Clear();
        SpawnRival();
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

    private string OverText()
    {
        if (_outcome == SnakeOutcome.Crashed)
        {
            return string.Format(GameText.Get("Games.Snake.Over", "Game over with {0} points. N to play again."), Score);
        }

        return _outcome switch
        {
            SnakeOutcome.Won => string.Format(GameText.Get("Games.Snake.VersusWon", "You win: {0} points to the computer's {1}. N to play again."), Score, RivalScore),
            SnakeOutcome.Lost => string.Format(GameText.Get("Games.Snake.VersusLost", "The computer wins: {0} points to your {1}. N to play again."), RivalScore, Score),
            _ => string.Format(GameText.Get("Games.Snake.VersusDraw", "Both crashed: you {0}, the computer {1}. N to play again."), Score, RivalScore),
        };
    }

    private void StartMoving()
    {
        _state = SnakeState.Moving;
        _accumulator = TimeSpan.Zero;
    }

    private void Step()
    {
        // The rival comes and goes with the setting, so it is brought in line before the snakes move.
        if (!VsComputer)
        {
            _rival.Clear();
        }
        else if (_rival.Count == 0)
        {
            SpawnRival();
        }

        _heading = _queued;
        (int X, int Y) head = Ahead(_body[0], _heading);
        bool rivalPlays = _rival.Count > 0;
        (int X, int Y) rivalHead = default;
        if (rivalPlays)
        {
            _rivalHeading = ChooseRivalHeading();
            rivalHead = Ahead(_rival[0], _rivalHeading);
        }

        bool eats = head == _food;
        bool rivalEats = rivalPlays && rivalHead == _food;
        bool crashed = Crashes(head, eats, _body, _rival, rivalPlays && rivalHead == head, rivalEats ? _rival.Count : _rival.Count - 1);
        bool rivalCrashed = rivalPlays && Crashes(rivalHead, rivalEats, _rival, _body, rivalHead == head, eats ? _body.Count : _body.Count - 1);
        if (crashed || rivalCrashed)
        {
            _state = SnakeState.Over;
            _outcome = !rivalPlays ? SnakeOutcome.Crashed
                : crashed && rivalCrashed ? SnakeOutcome.Draw
                : crashed ? SnakeOutcome.Lost
                : SnakeOutcome.Won;
            return;
        }

        _body.Insert(0, head);
        if (!eats)
        {
            _body.RemoveAt(_body.Count - 1);
        }

        if (rivalPlays)
        {
            _rival.Insert(0, rivalHead);
            if (!rivalEats)
            {
                _rival.RemoveAt(_rival.Count - 1);
            }
        }

        if (eats || rivalEats)
        {
            PlaceFood();
        }
    }

    private static (int X, int Y) Ahead((int X, int Y) cell, (int X, int Y) heading) => (cell.X + heading.X, cell.Y + heading.Y);

    private static bool OutOfBounds((int X, int Y) cell) => cell.X < 0 || cell.X >= Columns || cell.Y < 0 || cell.Y >= Rows;

    /// <summary>
    /// Whether a snake whose head moves to <paramref name="head"/> runs into a wall, its own body (the tail moves away unless it
    /// bites) or the other snake's body (the other tail moves away unless the other snake bites), or into the other head.
    /// </summary>
    private bool Crashes((int X, int Y) head, bool eats, List<(int X, int Y)> own, List<(int X, int Y)> other, bool headOnHead, int otherBlockingCount)
    {
        if (OutOfBounds(head) || headOnHead)
        {
            return true;
        }

        int ownBlocking = eats ? own.Count : own.Count - 1;
        for (int i = 0; i < ownBlocking; i++)
        {
            if (own[i] == head)
            {
                return true;
            }
        }

        if (other.Count > 0)
        {
            for (int i = 0; i < otherBlockingCount && i < other.Count; i++)
            {
                if (other[i] == head)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The rival keeps to free cells, turning towards the food and away from dead ends. It never reverses, and when every
    /// move is blocked it keeps its heading and crashes.
    /// </summary>
    private (int X, int Y) ChooseRivalHeading()
    {
        (int X, int Y) head = _rival[0];
        (int X, int Y) best = _rivalHeading;
        double bestScore = double.MinValue;
        foreach ((int X, int Y) direction in Directions)
        {
            if (direction == (-_rivalHeading.X, -_rivalHeading.Y))
            {
                continue;
            }

            (int X, int Y) next = Ahead(head, direction);
            if (OutOfBounds(next) || IsOccupied(next))
            {
                continue;
            }

            int room = FreeRoom(next);
            int distance = Math.Abs(next.X - _food.X) + Math.Abs(next.Y - _food.Y);
            double score = Math.Min(room, RoomCap) * 0.5 - distance + Random.NextDouble() * 2.5;
            if (score > bestScore)
            {
                bestScore = score;
                best = direction;
            }
        }

        return best;
    }

    private bool IsOccupied((int X, int Y) cell) => _body.Contains(cell) || _rival.Contains(cell);

    /// <summary>How many free cells can be reached from the cell, counted up to the cap.</summary>
    private int FreeRoom((int X, int Y) start)
    {
        HashSet<(int X, int Y)> seen = [start];
        Queue<(int X, int Y)> queue = new([start]);
        while (queue.Count > 0 && seen.Count < RoomCap)
        {
            (int X, int Y) cell = queue.Dequeue();
            foreach ((int X, int Y) direction in Directions)
            {
                (int X, int Y) next = Ahead(cell, direction);
                if (!OutOfBounds(next) && !IsOccupied(next) && seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return seen.Count;
    }

    /// <summary>The rival starts on the far side of the board, in a straight run of three that does not touch the player's snake.</summary>
    private void SpawnRival()
    {
        _rival.Clear();
        (int X, int Y) playerHead = _body[0];
        for (int y = 0; y < Rows; y++)
        {
            for (int x = Columns - 1; x >= StartLength - 1; x--)
            {
                List<(int X, int Y)> run = [(x, y), (x + 1, y), (x + 2, y)];
                if (x + 2 >= Columns || run.Any(c => _body.Contains(c) || c == _food) || Math.Abs(x - playerHead.X) + Math.Abs(y - playerHead.Y) < 6)
                {
                    continue;
                }

                _rival.AddRange(run);
                _rivalHeading = (-1, 0);
                return;
            }
        }
    }

    private void PlaceFood()
    {
        List<(int X, int Y)> free = [];
        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                if (!IsOccupied((x, y)))
                {
                    free.Add((x, y));
                }
            }
        }

        _food = free.Count == 0 ? (-1, -1) : free[Random.Next(free.Count)];
    }

    private GameCell CellAt((int X, int Y) cell)
    {
        if (VsComputer && _rival.Count > 0)
        {
            if (cell == _rival[0])
            {
                return new GameCell("  ", null, Color.Magenta);
            }

            if (_rival.Contains(cell))
            {
                return new GameCell("  ", null, Color.DarkMagenta);
            }
        }

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

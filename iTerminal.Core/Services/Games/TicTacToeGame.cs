using iConsole.Display;

namespace iTerminal.Core.Services.Games;

/// <summary>
/// Tic-Tac-Toe on the 3x3 board. X moves first. The computer searches the whole game tree, so it never loses; it picks
/// at random among equally good moves, which keeps its games from all looking alike.
/// </summary>
public class TicTacToeGame : GameBase
{
    private static readonly int[][] Lines =
    [
        [0, 1, 2], [3, 4, 5], [6, 7, 8],
        [0, 3, 6], [1, 4, 7], [2, 5, 8],
        [0, 4, 8], [2, 4, 6],
    ];

    private readonly Stack<char[]> _history = new();
    private char[] _board = new string(' ', 9).ToCharArray();
    private bool _xToMove = true;
    private int _cursor = 4;
    private int[]? _winningLine;
    private string _message = string.Empty;

    public TicTacToeGame() : this(null)
    {
    }

    internal TicTacToeGame(Random? random) : base(random)
    {
    }

    public override string Name => "Tic-Tac-Toe";

    public override string Rules => "Get three of your marks in a row, column or diagonal. X moves first.";

    public override bool IsOver => _winningLine is not null || _board.All(c => c != ' ');

    public override bool CanUndo => _history.Count > 0;

    public override bool IsComputerTurn => VsComputer && !IsOver && !_xToMove;

    internal char CellAt(int index) => _board[index];

    internal bool XToMove => _xToMove;

    public override string Status
    {
        get
        {
            if (_message.Length > 0)
            {
                return _message;
            }

            if (_winningLine is not null)
            {
                char winner = _board[_winningLine[0]];
                return $"{winner} wins!";
            }

            return IsOver ? "A draw." : $"{(_xToMove ? 'X' : 'O')} to move.";
        }
    }

    public override void NewGame()
    {
        Version++;
        _board = new string(' ', 9).ToCharArray();
        _xToMove = true;
        _winningLine = null;
        _history.Clear();
        _cursor = 4;
        _message = string.Empty;
    }

    public override void MoveCursor(int dx, int dy)
    {
        int x = Clamp(_cursor % 3 + dx, 2);
        int y = Clamp(_cursor / 3 + dy, 2);
        _cursor = y * 3 + x;
    }

    public override void Activate()
    {
        _message = string.Empty;
        if (IsOver || (VsComputer && !_xToMove))
        {
            return;
        }

        if (!Place(_cursor))
        {
            _message = "That square is taken.";
        }
    }

    internal bool Place(int index)
    {
        if (IsOver || index < 0 || index > 8 || _board[index] != ' ')
        {
            return false;
        }

        Version++;
        _history.Push((char[])_board.Clone());
        _board[index] = _xToMove ? 'X' : 'O';
        _xToMove = !_xToMove;
        _winningLine = FindWin(_board);
        return true;
    }

    public override void Undo()
    {
        _message = string.Empty;
        if (_history.Count == 0)
        {
            return;
        }

        Version++;
        RestoreLast();
        // Against the computer, take back its answer too, so it is the person's turn again.
        if (VsComputer && !_xToMove && _history.Count > 0)
        {
            RestoreLast();
        }
    }

    private void RestoreLast()
    {
        _board = _history.Pop();
        _xToMove = _board.Count(c => c != ' ') % 2 == 0;
        _winningLine = FindWin(_board);
    }

    private static int[]? FindWin(char[] board) =>
        Lines.FirstOrDefault(line => board[line[0]] != ' ' && board[line[0]] == board[line[1]] && board[line[1]] == board[line[2]]);

    public override IReadOnlyList<GameCell[]> Render()
    {
        List<GameCell[]> rows = [];
        for (int y = 0; y < 3; y++)
        {
            List<GameCell> row = [];
            for (int x = 0; x < 3; x++)
            {
                int index = y * 3 + x;
                char mark = _board[index];
                bool winning = _winningLine?.Contains(index) == true;
                Color? foreground = mark == 'X' ? Color.Cyan : mark == 'O' ? Color.Yellow : Color.DarkGray;
                GameCell cell = new($"  {(mark == ' ' ? '·' : mark)}  ", foreground, winning ? Color.DarkGreen : null);
                row.Add(index == _cursor ? cell.WithCursor() : cell);
                if (x < 2)
                {
                    row.Add(new GameCell("│"));
                }
            }

            rows.Add([.. row]);
            if (y < 2)
            {
                rows.Add([new GameCell("─────"), new GameCell("┼"), new GameCell("─────"), new GameCell("┼"), new GameCell("─────")]);
            }
        }

        return rows;
    }

    public override Func<CancellationToken, Action?>? PlanComputerMove()
    {
        if (!IsComputerTurn)
        {
            return null;
        }

        char[] snapshot = (char[])_board.Clone();
        int version = Version;
        int seed = Random.Next();
        return _ =>
        {
            int move = BestMove(snapshot, 'O', new Random(seed));
            return move < 0 ? null : () =>
            {
                if (version == Version)
                {
                    Place(move);
                }
            };
        };
    }

    /// <summary>The square the side to move should take: a best one by minimax, chosen at random among equals.</summary>
    internal static int BestMove(char[] board, char side, Random random)
    {
        List<int> best = [];
        int bestScore = int.MinValue;
        for (int i = 0; i < 9; i++)
        {
            if (board[i] != ' ')
            {
                continue;
            }

            board[i] = side;
            int score = -Negamax(board, Opponent(side), 1);
            board[i] = ' ';
            if (score > bestScore)
            {
                bestScore = score;
                best.Clear();
            }

            if (score == bestScore)
            {
                best.Add(i);
            }
        }

        return best.Count == 0 ? -1 : best[random.Next(best.Count)];
    }

    private static char Opponent(char side) => side == 'X' ? 'O' : 'X';

    /// <summary>Score for the side to move: positive if it wins (sooner is better), negative if it loses.</summary>
    private static int Negamax(char[] board, char side, int depth)
    {
        if (FindWin(board) is not null)
        {
            // The side to move has already lost: the other one just completed a line.
            return -(10 - depth);
        }

        int best = int.MinValue;
        for (int i = 0; i < 9; i++)
        {
            if (board[i] != ' ')
            {
                continue;
            }

            board[i] = side;
            best = Math.Max(best, -Negamax(board, Opponent(side), depth + 1));
            board[i] = ' ';
        }

        return best == int.MinValue ? 0 : best;
    }
}

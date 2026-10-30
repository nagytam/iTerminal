using iConsole.Display;

namespace iTerminal.Core.Services.Games;

/// <summary>
/// Chess with the whole rulebook: check, checkmate, stalemate, castling, en passant, promotion (you choose the piece),
/// the fifty-move rule, threefold repetition and dead positions. White is at the bottom and moves first; against the
/// computer you play White.
/// </summary>
public class ChessGame : GameBase
{
    private sealed record Snapshot(ChessState State, string LastMove, int KeyCount);

    private readonly Stack<Snapshot> _history = new();
    private readonly List<string> _keys = [];
    private ChessState _state = ChessState.Start();
    private List<ChessMove> _legal = [];
    private string? _result;
    private string _lastMove = string.Empty;
    private string _message = string.Empty;
    private int _cursorFile = 4;
    private int _cursorRank = 1;
    private int _selected = -1;
    private List<ChessMove>? _promotions;

    public ChessGame() : this(null)
    {
    }

    internal ChessGame(Random? random) : base(random)
    {
        NewGame();
    }

    public override string Name => "Chess";

    public override string Rules => "Select a piece and then where it goes. Castle by moving the king two squares. A pawn reaching the far rank is promoted: press Q, R, B or N.";

    public override bool IsOver => _result is not null;

    public override bool CanUndo => _history.Count > 0;

    public override bool IsComputerTurn => VsComputer && !IsOver && !_state.WhiteToMove;

    public override IEnumerable<(string Key, string Description)> ExtraKeys => [("Q R B N", "Choose the promotion piece")];

    internal ChessState State => _state;

    internal IReadOnlyList<ChessMove> LegalMoves => _legal;

    internal int Selected => _selected;

    internal bool AwaitsPromotion => _promotions is not null;

    internal string? Result => _result;

    internal int CursorSquare
    {
        get => _cursorRank * 8 + _cursorFile;
        set
        {
            _cursorFile = value & 7;
            _cursorRank = value >> 3;
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

            if (_result is not null)
            {
                return _result;
            }

            if (_promotions is not null)
            {
                return "Promote the pawn to: Q queen, R rook, B bishop, N knight.";
            }

            string side = _state.WhiteToMove ? "White" : "Black";
            string last = _lastMove.Length > 0 ? $" Last move {_lastMove}." : string.Empty;
            return (ChessRules.InCheck(_state) ? $"{side} is in check." : $"{side} to move.") + last;
        }
    }

    public override void NewGame()
    {
        Load(ChessState.Start());
        _cursorFile = 4;
        _cursorRank = 1;
    }

    /// <summary>Starts over from the given position.</summary>
    internal void Load(ChessState state)
    {
        Version++;
        _state = state;
        _history.Clear();
        _keys.Clear();
        _keys.Add(_state.Key());
        _lastMove = string.Empty;
        _message = string.Empty;
        _selected = -1;
        _promotions = null;
        Refresh();
    }

    /// <summary>Works out the legal moves and whether the game has ended.</summary>
    private void Refresh()
    {
        _legal = ChessRules.LegalMoves(_state);
        _result = null;
        if (_legal.Count == 0)
        {
            _result = ChessRules.InCheck(_state)
                ? $"Checkmate — {(_state.WhiteToMove ? "Black" : "White")} wins!"
                : "Stalemate — a draw.";
        }
        else if (_state.HalfMove >= 100)
        {
            _result = "A draw by the fifty-move rule.";
        }
        else if (ChessRules.InsufficientMaterial(_state))
        {
            _result = "A draw — neither side has the material to mate.";
        }
        else if (_keys.Count(k => k == _keys[^1]) >= 3)
        {
            _result = "A draw by threefold repetition.";
        }
    }

    public override void MoveCursor(int dx, int dy)
    {
        _cursorFile = Clamp(_cursorFile + dx, 7);
        _cursorRank = Clamp(_cursorRank - dy, 7);
    }

    public override void Activate()
    {
        _message = string.Empty;
        if (IsOver || _promotions is not null || (VsComputer && !_state.WhiteToMove))
        {
            return;
        }

        int square = CursorSquare;
        if (_selected == square)
        {
            _selected = -1;
            return;
        }

        if (_selected >= 0)
        {
            List<ChessMove> choices = [.. _legal.Where(m => m.From == _selected && m.To == square)];
            if (choices.Count > 1)
            {
                _promotions = choices;
                return;
            }

            if (choices.Count == 1)
            {
                Play(choices[0]);
                return;
            }
        }

        int piece = _state.Board[square];
        if (piece != 0 && (piece > 0) == _state.WhiteToMove)
        {
            _selected = _legal.Any(m => m.From == square) ? square : -1;
            if (_selected < 0)
            {
                _message = "That piece cannot move.";
            }
        }
        else if (_selected >= 0)
        {
            _message = "That is not a legal move.";
        }
    }

    public override bool TryChar(char c)
    {
        if (_promotions is null)
        {
            return false;
        }

        int type = char.ToLowerInvariant(c) switch
        {
            'q' => Piece.Queen,
            'r' => Piece.Rook,
            'b' => Piece.Bishop,
            'n' => Piece.Knight,
            _ => 0,
        };
        if (type == 0)
        {
            return false;
        }

        ChessMove choice = _promotions.First(m => m.Promotion == type);
        _promotions = null;
        Play(choice);
        return true;
    }

    /// <summary>Plays a legal move for the side to move.</summary>
    internal void Play(ChessMove move)
    {
        Version++;
        _history.Push(new Snapshot(_state, _lastMove, _keys.Count));
        _lastMove = move.ToString();
        _state = ChessRules.Apply(_state, move);
        _keys.Add(_state.Key());
        _selected = -1;
        _promotions = null;
        Refresh();
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
        if (VsComputer && !_state.WhiteToMove && _history.Count > 0)
        {
            Restore();
        }

        Refresh();
    }

    private void Restore()
    {
        Snapshot snapshot = _history.Pop();
        _state = snapshot.State;
        _lastMove = snapshot.LastMove;
        _keys.RemoveRange(snapshot.KeyCount, _keys.Count - snapshot.KeyCount);
        _selected = -1;
        _promotions = null;
    }

    public override IReadOnlyList<GameCell[]> Render()
    {
        List<GameCell[]> rows = [];
        int king = ChessRules.InCheck(_state) ? _state.KingSquare(_state.WhiteToMove) : -1;
        HashSet<int> targets = _selected < 0 ? [] : [.. _legal.Where(m => m.From == _selected).Select(m => m.To)];
        for (int rank = 7; rank >= 0; rank--)
        {
            List<GameCell> row = [new GameCell($" {rank + 1} ", Color.DarkGray)];
            for (int file = 0; file < 8; file++)
            {
                int square = rank * 8 + file;
                int piece = _state.Board[square];
                Color background = (file + rank) % 2 == 1 ? Color.DarkYellow : Color.DarkGray;
                string text = piece == 0 ? "   " : $" {Piece.Letter(piece)} ";
                Color foreground = piece > 0 ? Color.White : Color.Black;
                if (square == _selected)
                {
                    background = Color.Green;
                }
                else if (square == king)
                {
                    background = Color.Red;
                }
                else if (targets.Contains(square))
                {
                    background = Color.DarkGreen;
                    if (piece == 0)
                    {
                        text = " • ";
                        foreground = Color.White;
                    }
                }

                GameCell cell = new(text, foreground, background);
                row.Add(square == CursorSquare ? cell.WithCursor() : cell);
            }

            rows.Add([.. row]);
        }

        rows.Add([new GameCell("   "), .. Enumerable.Range(0, 8).Select(f => new GameCell($" {(char)('a' + f)} ", Color.DarkGray))]);
        return rows;
    }

    public override Func<CancellationToken, Action?>? PlanComputerMove()
    {
        if (!IsComputerTurn)
        {
            return null;
        }

        ChessState snapshot = _state;
        int version = Version;
        int seed = Random.Next();
        return token =>
        {
            ChessMove? move = new ChessSearch(new Random(seed), token, 1500).Best(snapshot);
            return move is null ? null : () =>
            {
                if (version == Version)
                {
                    Play(move.Value);
                }
            };
        };
    }
}

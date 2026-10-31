using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Services.Games;

namespace iTerminal.Core.Dialogs.Games;

/// <summary>
/// Board games to play inside the terminal: Tic-Tac-Toe, Chess, Go and Nine Men's Morris, against the computer or a
/// second person at the same keyboard. The games keep their positions while the app runs, so closing the dialog does
/// not end a game.
///
/// <para>Each <see cref="IGame"/> owns its rules and its drawing; this dialog forwards keys and paints what the game
/// renders. The computer thinks on a background task: it works on a snapshot of the position and parks the move it
/// chose, and the next idle pass plays it on the UI thread, so a key handler never waits on the search.</para>
/// </summary>
public class GamesDialog : SplitPaneDialog
{
    internal const string KeyHints = "Arrows:move  Enter:play  PgUp/PgDn:game  N:new  U:undo  M:mode  Esc:close";

    private const int MenuWidth = 24;

    private sealed record Outcome(int Generation, Action? Move);

    private readonly IGame[] _games;
    private int _current;

    private CancellationTokenSource? _cancellation;
    private Outcome? _pending;
    private int _generation;
    private volatile bool _thinking;
    private bool _stuck;

    public GamesDialog(IThemeService themeService)
        : this([new TicTacToeGame(), new ChessGame(), new GoGame(), new NineMensMorrisGame()], themeService)
    {
    }

    internal GamesDialog(IGame[] games, IThemeService themeService)
        : base(nameof(GamesDialog), themeService)
    {
        _games = games;
        Title = UiText.Get("Dialog.GamesTitle", "Games");
    }

    #region Test seams

    internal IGame Current => _games[_current];
    internal IReadOnlyList<IGame> Games => _games;
    internal bool IsThinking => _thinking;

    /// <summary>The search the dialog last started; nothing awaits it in the dialog, so a test waits on it instead of polling.</summary>
    internal Task ThinkingTask { get; private set; } = Task.CompletedTask;

    /// <summary>Plays the parked computer move now; the idle pass does the same in the running app.</summary>
    internal void ApplyPendingForTest() => OnIdle();

    #endregion

    public override void Close()
    {
        CancelThinking();
        base.Close();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.9);
        }

        base.OnInitializeLayout();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Arrows", UiText.Get("Hint.MoveCursor", "Move the cursor"));
        yield return ("Enter", UiText.Get("DialogHint.PlaySelectTake", "Play, select or take a piece at the cursor"));
        yield return ("Space", UiText.Get("DialogHint.SameAsEnter", "Same as Enter"));
        yield return ("PgUp / PgDn", UiText.Get("DialogHint.PreviousNextGame", "Previous or next game"));
        yield return ("1-4", UiText.Get("DialogHint.ChooseGame", "Choose a game"));
        yield return ("N", UiText.Get("DialogHint.StartGameAgain", "Start the game again"));
        yield return ("U", UiText.Get("DialogHint.TakeBackMove", "Take back the last move"));
        yield return ("M", UiText.Get("DialogHint.SwitchComputerTwoPlayers", "Switch between the computer and two players"));
        foreach ((string key, string description) in Current.ExtraKeys)
        {
            yield return (key, description);
        }
    }

    #region Keys

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down && !keyEvent.Control && !keyEvent.Alt && HandleGameKey(keyEvent))
        {
            StateHasChanged();
            return true;
        }

        bool handled = await base.HandleKeyEventAsync(keyEvent);
        StateHasChanged();
        return handled;
    }

    private bool HandleGameKey(KeyEvent keyEvent)
    {
        IGame game = Current;
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.LeftArrow:
                game.MoveCursor(-1, 0);
                return true;
            case ConsoleKey.RightArrow:
                game.MoveCursor(1, 0);
                return true;
            case ConsoleKey.UpArrow:
                game.MoveCursor(0, -1);
                return true;
            case ConsoleKey.DownArrow:
                game.MoveCursor(0, 1);
                return true;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                _stuck = false;
                game.Activate();
                return true;
            case ConsoleKey.PageUp:
                SelectGame((_current + _games.Length - 1) % _games.Length);
                return true;
            case ConsoleKey.PageDown:
                SelectGame((_current + 1) % _games.Length);
                return true;
        }

        char c = keyEvent.KeyChar;
        if (c >= '1' && c < '1' + _games.Length)
        {
            SelectGame(c - '1');
            return true;
        }

        if (!char.IsLetter(c))
        {
            return false;
        }

        _stuck = false;
        if (game.TryChar(c))
        {
            return true;
        }

        switch (char.ToLowerInvariant(c))
        {
            case 'n':
                CancelThinking();
                game.NewGame();
                return true;
            case 'u':
                CancelThinking();
                game.Undo();
                return true;
            case 'm':
                CancelThinking();
                game.VsComputer = !game.VsComputer;
                return true;
            default:
                return false;
        }
    }

    internal void SelectGame(int index)
    {
        if (index == _current)
        {
            return;
        }

        CancelThinking();
        _current = index;
    }

    #endregion

    #region The computer's turn

    public override void OnIdle()
    {
        base.OnIdle();

        Outcome? outcome = Interlocked.Exchange(ref _pending, null);
        if (outcome is not null && outcome.Generation == _generation)
        {
            _thinking = false;
            if (outcome.Move is { } move)
            {
                move();
            }
            else
            {
                _stuck = true;
            }
        }

        if (!_thinking && !_stuck && Current.IsComputerTurn)
        {
            StartThinking();
        }
    }

    private void StartThinking()
    {
        Func<CancellationToken, Action?>? plan = Current.PlanComputerMove();
        if (plan is null)
        {
            return;
        }

        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;
        int generation = ++_generation;
        _thinking = true;
        StateHasChanged();

        ThinkingTask = Task.Run(() =>
        {
            Action? move = null;
            try
            {
                move = plan(token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // The computer simply passes the turn back; the person can still undo or start again.
            }

            _pending = new Outcome(generation, move);
        });
    }

    /// <summary>Drops the search in flight and any move it parked.</summary>
    private void CancelThinking()
    {
        _cancellation?.Cancel();
        _generation++;
        _pending = null;
        _thinking = false;
    }

    #endregion

    #region Presentation

    internal static IEnumerable<string> Wrap(string text, int width)
    {
        if (width <= 0)
        {
            yield break;
        }

        string line = string.Empty;
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line;
                line = string.Empty;
            }

            line = line.Length == 0 ? word : line + " " + word;
            while (line.Length > width)
            {
                yield return line[..width];
                line = line[width..];
            }
        }

        if (line.Length > 0)
        {
            yield return line;
        }
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int ix = (int)AbsolutePosition.X + 1;
        int iy = (int)AbsolutePosition.Y + 1;
        int iw = (int)ViewSize.Width - 2;
        int ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 0)
        {
            return;
        }

        for (int row = 0; row < ih - 1; row++)
        {
            WriteLine(buffer, ix, iy + row, iw, string.Empty, colorScheme.Foreground, colorScheme.Background);
        }

        IGame game = Current;
        int menuWidth = iw >= 64 ? MenuWidth : 0;
        if (menuWidth > 0)
        {
            DrawMenu(buffer, ix, iy, menuWidth, ih - 1, colorScheme);
        }

        int x0 = ix + (menuWidth > 0 ? menuWidth + 2 : 0);
        int width = iw - (x0 - ix);
        if (width <= 0)
        {
            return;
        }

        string mode = game.VsComputer ? "against the computer" : "two players";
        DrawSectionHeader(buffer, x0, iy, width, $"{game.Name} — {mode}", true, colorScheme);

        string status = game.Status + (_thinking ? " The computer is thinking…" : string.Empty);
        int line = 1;
        foreach (string part in Wrap(status, width - 1).Take(2))
        {
            if (line < ih - 1)
            {
                WriteLine(buffer, x0, iy + line, width, " " + part, game.IsOver ? Color.Yellow : Color.Cyan, colorScheme.Background);
            }

            line++;
        }

        int boardTop = 4;
        IReadOnlyList<GameCell[]> rows = game.Render();
        int boardWidth = rows.Count == 0 ? 0 : rows.Max(r => r.Sum(c => c.Text.Length));
        int left = x0 + Math.Max(0, (width - boardWidth) / 2);
        for (int r = 0; r < rows.Count && boardTop + r < ih - 1; r++)
        {
            int x = left;
            foreach (GameCell cell in rows[r])
            {
                int room = x0 + width - x;
                if (room <= 0)
                {
                    break;
                }

                string text = cell.Text.Length > room ? cell.Text[..room] : cell.Text;
                Color foreground = cell.Foreground ?? colorScheme.Foreground;
                Color background = cell.Background ?? colorScheme.Background;
                if (cell.Cursor)
                {
                    foreground = Color.Black;
                    background = Color.Cyan;
                }

                buffer.Write(ZOrder, new Position(x, iy + boardTop + r), new Size(text.Length, 1), new ConxelString(text, foreground, background));
                x += text.Length;
            }
        }

        int rulesTop = boardTop + rows.Count + 1;
        foreach (string part in Wrap(game.Rules, width - 1))
        {
            if (rulesTop < ih - 1)
            {
                WriteLine(buffer, x0, iy + rulesTop, width, " " + part, colorScheme.DisabledForeground, colorScheme.Background);
            }

            rulesTop++;
        }

        DrawStatusAndHints(buffer, ix, iy + ih - 1, iw, string.Empty, colorScheme.Foreground, KeyHints, colorScheme);
    }

    private void DrawMenu(IBuffer buffer, int x, int y, int width, int height, ColorScheme colorScheme)
    {
        DrawSectionHeader(buffer, x, y, width, "Games", false, colorScheme);
        for (int i = 0; i < _games.Length && 1 + i < height; i++)
        {
            bool selected = i == _current;
            WriteLine(buffer, x, y + 1 + i, width, $" {i + 1}  {_games[i].Name}",
                selected ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground,
                selected ? colorScheme.SelectedFocusedBackground : colorScheme.Background);
        }

        for (int row = 0; row < height; row++)
        {
            WriteLine(buffer, x + width, y + row, 1, "│", colorScheme.DisabledForeground, colorScheme.Background);
        }
    }

    #endregion
}

using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Games;

namespace iTerminal.Core.Dialogs.Games;

/// <summary>
/// Board games to play inside the terminal: Tic-Tac-Toe, Chess, Go, Nine Men's Morris and Snake. The first four play
/// against the computer or a second person at the same keyboard; Snake is for one person. The games keep their positions
/// while the app runs, so closing the dialog does not end a game.
///
/// <para>Each <see cref="IGame"/> owns its rules and its drawing; this dialog forwards keys and paints what the game
/// renders. The computer thinks on a background task: it works on a snapshot of the position and parks the move it
/// chose, and the next idle pass plays it on the UI thread, so a key handler never waits on the search.</para>
///
/// <para>A game that moves on its own clock (Snake) is ticked from <see cref="OnIdle"/> with the time since the last
/// idle pass, capped at one second. The idle passes may come less often while the screen changes, so a game must not
/// count passes; the elapsed time is what moves it.</para>
/// </summary>
public abstract class GamesDialog : SplitPaneDialog
{
    private const int MenuWidth = 24;

    /// <summary>Most status lines shown; a longer status ends on an ellipsis.</summary>
    private const int MaxStatusRows = 3;

    /// <summary>Rows kept for the rules under the board, so a tall board does not leave them none.</summary>
    private const int RulesRowsKept = 2;


    /// <summary>The longest stretch of time one idle pass hands to the game, so a stall does not make it jump.</summary>
    private const long MaxTickMilliseconds = 1000;

    private sealed record Outcome(int Generation, Action? Move);

    private readonly IGame[] _games;
    private readonly ITimeProvider _time;
    private long _lastMs;
    private int _current;

    private CancellationTokenSource? _cancellation;
    private readonly Lock _landGate = new();
    private Outcome? _pending;
    private int _generation;
    private volatile bool _thinking;
    private bool _stuck;
    private bool _confirmingNewGame;

    /// <summary>Each game opens in a dialog of its own: the concrete dialogs pass their one game here. With several games the dialog also shows a game list.</summary>
    protected internal GamesDialog(string dialogName, string title, IGame[] games, IThemeService themeService, ITimeProvider? timeProvider = null)
        : base(dialogName, themeService)
    {
        _games = games;
        _time = timeProvider ?? new SystemTimeProvider();
        Title = title;
    }

    private bool HasSeveralGames => _games.Length > 1;

    #region Test seams

    internal IGame Current => _games[_current];
    internal IReadOnlyList<IGame> Games => _games;
    internal bool IsThinking => _thinking;

    /// <summary>The search the dialog last started; nothing awaits it in the dialog, so a test waits on it instead of polling.</summary>
    internal Task ThinkingTask { get; private set; } = Task.CompletedTask;

    /// <summary>Plays the parked computer move now; the idle pass does the same in the running app.</summary>
    internal void ApplyPendingForTest() => OnIdle();

    #endregion

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        // Time that passed while the dialog was closed does not move a game when it opens again.
        _lastMs = _time.MonotonicMilliseconds;
    }

    public override void Close()
    {
        CancelThinking();
        _confirmingNewGame = false;
        Current.Pause();
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
        yield return ("Enter", Current.ActivateHint);
        yield return ("Space", UiText.Get("DialogHint.SameAsEnter", "Same as Enter"));
        if (HasSeveralGames)
        {
            yield return ("PgUp / PgDn", UiText.Get("DialogHint.PreviousNextGame", "Previous or next game"));
            yield return ($"1-{_games.Length}", UiText.Get("DialogHint.ChooseGame", "Choose a game"));
        }

        yield return ("N", UiText.Get("DialogHint.StartGameAgain", "Start the game again"));
        if (Current.OffersUndo)
        {
            yield return ("U", UiText.Get("DialogHint.TakeBackMove", "Take back the last move"));
        }

        if (Current.OffersComputerOpponent)
        {
            yield return ("M", UiText.Get("DialogHint.SwitchComputerTwoPlayers", "Switch between the computer and two players"));
        }

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
        if (_confirmingNewGame)
        {
            // Only y starts over. Esc and n keep the game; any other key drops the question and is handled as usual.
            _confirmingNewGame = false;
            char answer = char.ToLowerInvariant(keyEvent.KeyChar);
            if (answer == 'y')
            {
                CancelThinking();
                game.NewGame();
                return true;
            }

            if (answer == 'n' || keyEvent.ConsoleKey == ConsoleKey.Escape)
            {
                return true;
            }
        }

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
            case ConsoleKey.PageUp when HasSeveralGames:
                SelectGame((_current + _games.Length - 1) % _games.Length);
                return true;
            case ConsoleKey.PageDown when HasSeveralGames:
                SelectGame((_current + 1) % _games.Length);
                return true;
        }

        char c = keyEvent.KeyChar;
        if (HasSeveralGames && c >= '1' && c < '1' + _games.Length)
        {
            SelectGame(c - '1');
            return true;
        }

        if (game.TryChar(c))
        {
            _stuck = false;
            return true;
        }

        if (!char.IsLetter(c))
        {
            return false;
        }

        _stuck = false;
        switch (char.ToLowerInvariant(c))
        {
            case 'n':
                if (game.InProgress)
                {
                    // Snake and Tetris keep falling or moving while the question is up, so they stop first.
                    game.Pause();
                    _confirmingNewGame = true;
                    return true;
                }

                CancelThinking();
                game.NewGame();
                return true;
            case 'u':
                if (!game.OffersUndo)
                {
                    return false;
                }

                CancelThinking();
                game.Undo();
                return true;
            case 'm':
                if (!game.OffersComputerOpponent)
                {
                    return false;
                }

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
        _confirmingNewGame = false;
        Current.Pause();
        _current = index;
    }

    #endregion

    #region The computer's turn

    public override void OnIdle()
    {
        base.OnIdle();

        long now = _time.MonotonicMilliseconds;
        TimeSpan delta = TimeSpan.FromMilliseconds(Math.Min(now - _lastMs, MaxTickMilliseconds));
        _lastMs = now;
        if (Current.Tick(delta))
        {
            StateHasChanged();
        }

        if (_confirmingNewGame && !Current.InProgress)
        {
            // The game ended while the question was up: there is nothing left to lose, so the question goes.
            _confirmingNewGame = false;
        }

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
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;
        int generation = NextGeneration();
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

            Land(new Outcome(generation, move));
        });
    }

    /// <summary>Drops the search in flight and any move it parked.</summary>
    private void CancelThinking()
    {
        _cancellation?.Cancel();
        NextGeneration();
        _pending = null;
        _thinking = false;
    }

    private int NextGeneration()
    {
        lock (_landGate)
        {
            return ++_generation;
        }
    }

    private void Land(Outcome outcome)
    {
        // A search that was cancelled still finishes. Only the newest one may park its move: a late one from the search it
        // replaced would overwrite the move the newer search has just parked, and the computer would wait for ever.
        lock (_landGate)
        {
            if (outcome.Generation == _generation)
            {
                _pending = outcome;
            }
        }
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

    /// <summary>The column the cursor's cell starts at, within its row; 0 when there is none.</summary>
    private static int CursorColumn(IReadOnlyList<GameCell[]> rows, int row)
    {
        if (row >= rows.Count)
        {
            return 0;
        }

        int column = 0;
        foreach (GameCell cell in rows[row])
        {
            if (cell.Cursor)
            {
                return column + cell.Text.Length / 2;
            }

            column += cell.Text.Length;
        }

        return 0;
    }

    /// <summary>Paints the part of one board row that lies in the view: from colOffset, viewWidth columns wide.</summary>
    private void DrawBoardRow(IBuffer buffer, GameCell[] cells, int left, int y, int colOffset, int viewWidth, ColorScheme colorScheme)
    {
        int column = 0;
        foreach (GameCell cell in cells)
        {
            int start = column;
            int end = column + cell.Text.Length;
            column = end;
            int from = Math.Max(start, colOffset);
            int to = Math.Min(end, colOffset + viewWidth);
            if (from >= to)
            {
                continue;
            }

            string text = cell.Text.Substring(from - start, to - from);
            Color foreground = cell.Foreground ?? colorScheme.Foreground;
            Color background = cell.Background ?? colorScheme.Background;
            if (cell.Cursor)
            {
                foreground = Color.Black;
                background = Color.Cyan;
            }

            buffer.Write(ZOrder, new Position(left + from - colOffset, y), new Size(text.Length, 1), new ConxelString(text, foreground, background));
        }
    }

    /// <summary>
    /// Ends text that goes on past the room it has on an ellipsis, cutting it to fit max columns first. The cut falls
    /// between words, so the ellipsis never splits one.
    /// </summary>
    private static string WithEllipsis(string text, int max)
    {
        int limit = Math.Max(0, max - 2);
        string kept = text;
        if (text.Length > limit)
        {
            int space = text.LastIndexOf(' ', Math.Min(limit, text.Length - 1));
            kept = space > 0 ? text[..space] : text[..limit];
        }

        return kept.TrimEnd() + " …";
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
        int menuWidth = HasSeveralGames && iw >= 64 ? MenuWidth : 0;
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

        string mode = game.ModeLabel ?? (!game.OffersComputerOpponent
            ? UiText.Get("Games.OnePlayer", "one player")
            : game.VsComputer ? UiText.Get("Games.Mode.VsComputer", "against the computer") : UiText.Get("Games.Mode.TwoPlayers", "two players"));
        DrawSectionHeader(buffer, x0, iy, width, $"{game.Name} — {mode}", true, colorScheme);

        // Without room for the game list, a line says which game this is out of how many, so the others are not hidden.
        int line = 1;
        if (menuWidth == 0 && HasSeveralGames && ih > 2)
        {
            WriteLine(buffer, x0, iy + line, width, " " + string.Format(UiText.Get("Games.Position", "Game {0} of {1}"), _current + 1, _games.Length) + "  " + UiText.Get("Games.HintGame", "PgUp/PgDn:game"), colorScheme.DisabledForeground, colorScheme.Background);
            line++;
        }

        int statusTop = line;
        string status = _confirmingNewGame
            ? UiText.Get("Games.ConfirmNew", "A game is on. Start a new one and lose this one? (y/n)")
            : game.Status + (_thinking ? " " + UiText.Get("Games.Thinking", "The computer is thinking…") : string.Empty);
        Color statusColor = _confirmingNewGame || game.IsOver ? Color.Yellow : Color.Cyan;
        List<string> statusLines = [.. Wrap(status, width - 1)];
        if (statusLines.Count > MaxStatusRows)
        {
            statusLines = statusLines.Take(MaxStatusRows).ToList();
            statusLines[^1] = WithEllipsis(statusLines[^1], width - 2);
        }

        for (int i = 0; i < statusLines.Count; i++)
        {
            if (statusTop + i < ih - 1)
            {
                WriteLine(buffer, x0, iy + statusTop + i, width, " " + statusLines[i], statusColor, colorScheme.Background);
            }
        }

        // The board gets the rows under the status, but leaves the rules a couple of lines; a board taller than that scrolls
        // so the cursor stays on it, and the edges it is cut at are marked.
        int boardTop = statusTop + statusLines.Count + 1;
        int boardRoom = Math.Max(0, ih - 1 - boardTop - RulesRowsKept);
        IReadOnlyList<GameCell[]> rows = game.Render();
        int boardWidth = rows.Count == 0 ? 0 : rows.Max(r => r.Sum(c => c.Text.Length));
        int shownRows = Math.Min(rows.Count, boardRoom);
        int cursorRow = Math.Max(0, rows.Select((r, i) => (r, i)).FirstOrDefault(p => p.r.Any(c => c.Cursor)).i);
        int rowOffset = rows.Count <= shownRows ? 0 : Math.Clamp(cursorRow - shownRows / 2, 0, rows.Count - shownRows);
        int shownWidth = Math.Min(boardWidth, width);
        int colOffset = boardWidth <= width ? 0 : Math.Clamp(CursorColumn(rows, cursorRow) - width / 2, 0, boardWidth - width);
        int left = boardWidth <= width ? x0 + (width - boardWidth) / 2 : x0;
        for (int r = 0; r < shownRows; r++)
        {
            DrawBoardRow(buffer, rows[rowOffset + r], left, iy + boardTop + r, colOffset, shownWidth, colorScheme);
        }

        Color edge = colorScheme.DisabledForeground;
        if (shownRows > 0 && rowOffset > 0)
        {
            WriteLine(buffer, x0 + width - 1, iy + boardTop, 1, "↑", edge, colorScheme.Background);
        }

        if (shownRows > 0 && rowOffset + shownRows < rows.Count)
        {
            WriteLine(buffer, x0 + width - 1, iy + boardTop + shownRows - 1, 1, "↓", edge, colorScheme.Background);
        }

        if (shownRows > 0 && colOffset > 0)
        {
            WriteLine(buffer, x0, iy + boardTop + shownRows / 2, 1, "←", edge, colorScheme.Background);
        }

        if (shownRows > 0 && colOffset + width < boardWidth)
        {
            WriteLine(buffer, x0 + width - 1, iy + boardTop + shownRows / 2, 1, "→", edge, colorScheme.Background);
        }

        List<string> ruleLines = [.. Wrap(game.Rules, width - 1)];
        // The blank line above the rules gives way when the rules would otherwise lose a line they need.
        int rulesTop = boardTop + shownRows + 1;
        if (ih - 1 - rulesTop < ruleLines.Count && ih - 1 - (rulesTop - 1) >= ruleLines.Count)
        {
            rulesTop--;
        }

        int rulesRoom = Math.Max(0, ih - 1 - rulesTop);
        for (int i = 0; i < ruleLines.Count && i < rulesRoom; i++)
        {
            string part = ruleLines[i];
            if (i == rulesRoom - 1 && ruleLines.Count > rulesRoom)
            {
                // More to say than the dialog has rows for: end on an ellipsis rather than a sentence cut in the air.
                part = WithEllipsis(part, width - 2);
            }

            WriteLine(buffer, x0, iy + rulesTop + i, width, " " + part, colorScheme.DisabledForeground, colorScheme.Background);
        }

        DrawStatusAndHints(buffer, ix, iy + ih - 1, iw, string.Empty, colorScheme.Foreground, FooterHints(), colorScheme);
    }

    /// <summary>The footer for the game on screen: the keys it answers to, short.</summary>
    internal string FooterHints()
    {
        IGame game = Current;
        List<string> hints = [iConsole.Localization.UiText.Get("Games.HintMove", "Arrows:move"), string.Format(iConsole.Localization.UiText.Get("Games.HintEnter", "Enter:{0}"), game.ActivateLabel)];
        if (HasSeveralGames)
        {
            hints.Add(iConsole.Localization.UiText.Get("Games.HintGame", "PgUp/PgDn:game"));
        }

        hints.Add(iConsole.Localization.UiText.Get("Games.HintNew", "N:new"));
        if (game.OffersUndo)
        {
            hints.Add(iConsole.Localization.UiText.Get("Games.HintUndo", "U:undo"));
        }

        if (game.OffersComputerOpponent)
        {
            hints.Add(iConsole.Localization.UiText.Get("Games.HintMode", "M:mode"));
        }

        hints.Add(iConsole.Localization.UiText.Get("Hint.Part.EscClose", "Esc:close"));
        return string.Join("  ", hints);
    }

    private void DrawMenu(IBuffer buffer, int x, int y, int width, int height, ColorScheme colorScheme)
    {
        DrawSectionHeader(buffer, x, y, width, UiText.Get("Games.Header", "Games"), false, colorScheme);
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

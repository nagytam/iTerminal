using iConsole.Localization;
using iTerminal.Core.Helper;
using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.AI;
using iTerminal.Core.Commands;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Dialogs;
using System.ComponentModel;

namespace iTerminal.Core.Controls.Terminals;

public class AiPromptBar : Window
{
    /// <summary>Columns of the first row: "AI" (0-1), the spinner (2), then the model label and the prompt label share the rest.</summary>
    private const int AiLabelWidth = 2;
    private const int SpinnerColumn = AiLabelWidth;
    private const int ModelLabelColumn = SpinnerColumn + 1;

    private readonly AiChatModel _model;
    private readonly IAiChatService _chatService;
    private readonly Label _aiLabel;
    private readonly Spinner _spinner;
    private readonly Label _modelLabel;
    private readonly Label _promptLabel;
    private CancellationTokenSource? _cts;

    /// <summary>First response line shown; reset whenever a new response starts arriving.</summary>
    private int _responseScroll;

    /// <summary>The questions sent so far, oldest first, for Up and Down to recall.</summary>
    private readonly List<string> _history = [];
    private const int HistoryLimit = 50;
    private int _historyIndex = -1;
    private string _draft = string.Empty;

    private void Remember(string input)
    {
        string question = input.Trim();
        _historyIndex = -1;
        if (question.Length == 0)
        {
            return;
        }

        if (_history.Count == 0 || _history[^1] != question)
        {
            _history.Add(question);
        }

        if (_history.Count > HistoryLimit)
        {
            _history.RemoveAt(0);
        }
    }
    private string _lastResponseSeen = string.Empty;

    public AiPromptBar(
        AiChatModel model,
        IAiChatService chatService,
        AiModelDialog aiModelDialog,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService) : base(themeService)
    {
        _model = model;
        _chatService = chatService;
        _model.PropertyChanged += OnModelChanged;
        _wasCollapsed = IsCollapsed;
        CanFocus = true;
        AddCommand(new AiModelDialogCommand(aiModelDialog));
        AddCommand(new TabForwardCommand());
        AddCommand(new TabBackwardCommand());

        _aiLabel = new Label("AI", themeService);
        _spinner = new Spinner(themeService, dateTimeProviderService) { IsActive = false };
        _modelLabel = new Label(string.Empty, themeService);
        _promptLabel = new Label(string.Empty, themeService);
        AddControl(_aiLabel);
        AddControl(_spinner);
        AddControl(_modelLabel);
        AddControl(_promptLabel);
    }

    public override void OnInitializeLayout()
    {
        _aiLabel.Position = new Position(0, 0);
        _aiLabel.ViewSize = new Size(AiLabelWidth, 1);
        _spinner.Position = new Position(SpinnerColumn, 0);
        _spinner.ViewSize = new Size(1, 1);
        base.OnInitializeLayout();
    }

    public override void OnHandleTabForward()
    {
        var parent = Parent;
        if (parent is null) return;
        var focusable = parent.Controls
            .Where(c => (c is FilePanel || c is AiPromptBar || c is TerminalPanel) && c.IsVisible)
            .ToList();
        var idx = focusable.IndexOf(this);
        focusable[(idx + 1) % focusable.Count]?.SetFocus();
    }

    public override void OnHandleTabBackward()
    {
        var parent = Parent;
        if (parent is null) return;
        var focusable = parent.Controls
            .Where(c => (c is FilePanel || c is AiPromptBar || c is TerminalPanel) && c.IsVisible)
            .ToList();
        var idx = focusable.IndexOf(this);
        focusable[(idx - 1 + focusable.Count) % focusable.Count]?.SetFocus();
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        _ = Task.Run(async () => await _chatService.CheckAvailabilityAsync(_model));
    }

    /// <summary>The text the availability check starts with, before it has an answer.</summary>
    private const string CheckingModelInfo = "checking...";

    /// <summary>
    /// True once the check has answered "no model here". The bar then needs one row, not the
    /// configured height: it used to keep all five, four of them blank under a one-line
    /// "[server error (401)]", taking that much of every listing for an assistant that was not there.
    /// </summary>
    public bool IsCollapsed => !_model.IsAvailable && _model.ModelInfo != CheckingModelInfo;

    /// <summary>The rows the bar wants out of <paramref name="configuredHeight"/>.</summary>
    public int PreferredHeight(int configuredHeight) => IsCollapsed ? Math.Min(1, configuredHeight) : configuredHeight;

    /// <summary>Raised when <see cref="IsCollapsed"/> changes, so the parent can lay the panels out again.</summary>
    public event EventHandler? PreferredHeightChanged;

    private bool _wasCollapsed;

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        StateHasChanged();
        var collapsed = IsCollapsed;
        if (collapsed != _wasCollapsed)
        {
            _wasCollapsed = collapsed;
            PreferredHeightChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!_model.IsAvailable)
        {
            yield break;
        }

        yield return ("Enter", UiText.Get("DialogHint.SendQuestion", "Send the question"));
        yield return ("Esc", UiText.Get("DialogHint.ClearQuestionCancelAnswer", "Clear the question, or cancel the answer"));
        yield return ("Up/Down", UiText.Get("DialogHint.RecallQuestions", "Recall the questions already sent"));
        yield return ("PageUp/PageDown", UiText.Get("DialogHint.ScrollAnswer", "Scroll the answer"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled) return true;

        // Only with the focus: the bar sits beside the panels, and an unfocused bar used to take the typed characters,
        // Escape (which cancels a running answer) and Enter (which sends the question) meant for whoever has the focus.
        if (!_model.IsAvailable || !IsFocused) return false;

        if (PasteHelper.IsDeleteWordKey(keyEvent) || PasteHelper.IsClearKey(keyEvent))
        {
            _model.Input = PasteHelper.IsClearKey(keyEvent) ? string.Empty : PasteHelper.DeleteLastWord(_model.Input);
            return true;
        }

        if (PasteHelper.IsPasteKey(keyEvent))
        {
            PasteHelper.Paste(text => _model.Input += PasteHelper.SingleLine(text), StateHasChanged);
            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Enter:
                if (!_model.IsThinking && !string.IsNullOrWhiteSpace(_model.Input))
                {
                    if (IsQuitCommand(_model.Input))
                    {
                        _model.Input = string.Empty;
                        await QuitAsync();
                        return true;
                    }

                    Remember(_model.Input);
                    _ = SendAsync();
                }
                return true;

            case ConsoleKey.Escape:
                _cts?.Cancel();
                _model.Input = string.Empty;
                _historyIndex = -1;
                return true;

            // Up and Down walk the questions already sent (only with the focus: the keys are the panels' otherwise);
            // Down past the newest brings back what was being typed.
            case ConsoleKey.UpArrow when IsFocused && _history.Count > 0 && !keyEvent.Control && !keyEvent.Alt:
                if (_historyIndex < 0)
                {
                    _draft = _model.Input;
                    _historyIndex = _history.Count - 1;
                }
                else if (_historyIndex > 0)
                {
                    _historyIndex--;
                }

                _model.Input = _history[_historyIndex];
                return true;

            case ConsoleKey.DownArrow when IsFocused && _historyIndex >= 0 && !keyEvent.Control && !keyEvent.Alt:
                if (_historyIndex < _history.Count - 1)
                {
                    _historyIndex++;
                    _model.Input = _history[_historyIndex];
                }
                else
                {
                    _historyIndex = -1;
                    _model.Input = _draft;
                }

                return true;

            case ConsoleKey.Backspace:
                if (_model.Input.Length > 0)
                    _model.Input = _model.Input[..^1];
                return true;

            // A response longer than the bar used to be cut at its last row with no way to read the
            // rest; the bar only ever showed the beginning of an answer.
            case ConsoleKey.PageUp:
                ScrollResponse(-Math.Max(1, ResponseRows - 1));
                return true;

            case ConsoleKey.PageDown:
                ScrollResponse(Math.Max(1, ResponseRows - 1));
                return true;

            case ConsoleKey.F1:
            case ConsoleKey.F2:
            case ConsoleKey.F3:
            case ConsoleKey.F4:
            case ConsoleKey.F5:
            case ConsoleKey.F6:
            case ConsoleKey.F7:
            case ConsoleKey.F8:
            case ConsoleKey.F9:
            case ConsoleKey.F10:
            case ConsoleKey.F11:
            case ConsoleKey.F12:
                return false;

            default:
                // Printable characters only: Esc, Tab and friends carry a KeyChar too, and used to
                // be typed into the prompt as invisible control characters.
                if (keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar) && !keyEvent.Control && !keyEvent.Alt)
                {
                    _model.Input += keyEvent.KeyChar;
                    return true;
                }
                return false;
        }
    }

    private int ResponseRows => Math.Max(0, (int)ViewSize.Height - 1);

    /// <summary>Index of the first response line on screen.</summary>
    internal int ResponseScroll => _responseScroll;

    private List<string> ResponseLines() => _model.LastResponse.WrapWords((int)ViewSize.Width);

    private void ScrollResponse(int delta)
    {
        var maxScroll = Math.Max(0, ResponseLines().Count - ResponseRows);
        _responseScroll = Math.Clamp(_responseScroll + delta, 0, maxScroll);
        StateHasChanged();
    }

    private async Task SendAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        await _chatService.SendMessageAsync(_model, _cts.Token);
    }

    /// <summary>
    /// "quit" / "exit" entered in the AI prompt are treated as a request to close the
    /// application rather than a message to the model.
    /// </summary>
    private static bool IsQuitCommand(string input)
    {
        var trimmed = input.Trim();
        return trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase);
    }

    private async Task QuitAsync()
    {
        // Reuse the same QuitCommand that F10 triggers; it resolves the desktop from this
        // control's root and shuts the application down.
        await new QuitCommand { Control = this }.RunAsync(null);
    }

    public override void Draw(IBuffer buffer)
    {
        if (ViewSize.IsEmpty) return;

        var focused = IsFocused;
        var bg = focused ? Color.Black : Color.Gray;
        var width = (int)ViewSize.Width;

        if (!_model.IsAvailable)
        {
            var warningFg = Color.DarkYellow;
            var modelInfoStr = _model.ModelInfo;
            _spinner.IsActive = modelInfoStr == CheckingModelInfo;

            // Blank the whole control first so nothing but the four composed pieces is visible.
            BlankRows(buffer, warningFg, bg, width);

            _aiLabel.ForegroundColor = warningFg;
            _aiLabel.BackgroundColor = bg;
            _spinner.Foreground = warningFg;
            _spinner.Background = bg;

            var warningModelWidth = Math.Max(0, width - ModelLabelColumn);
            // Say what is wrong and where to look: "[server error (401)]" alone named neither the
            // assistant nor a way to find out more.
            // The hints go first when it does not fit — the reason is the part worth the room.
            var reason = IsCollapsed ? $" unavailable: {modelInfoStr}" : $" [{modelInfoStr}]";
            var withHints = reason + "  —  Ctrl+F2: details, Ctrl+B: terminal bar";
            var warning = IsCollapsed && withHints.Length <= warningModelWidth ? withHints : reason;
            _modelLabel.Text = warning.Shrink(warningModelWidth)?.ExpandRight(warningModelWidth) ?? string.Empty;
            _modelLabel.ForegroundColor = warningFg;
            _modelLabel.BackgroundColor = bg;
            _modelLabel.Position = new Position(ModelLabelColumn, 0);
            _modelLabel.ViewSize = new Size(warningModelWidth, 1);

            _aiLabel.Draw(buffer);
            _spinner.Draw(buffer);
            _modelLabel.Draw(buffer);
            return;
        }

        var inputFg = focused ? Color.Yellow : Color.DarkYellow;
        var responseFg = Color.Cyan;

        BlankRows(buffer, inputFg, bg, width);

        _aiLabel.ForegroundColor = inputFg;
        _aiLabel.BackgroundColor = bg;

        _spinner.IsActive = _model.IsThinking;
        _spinner.Foreground = inputFg;
        _spinner.Background = bg;

        // Model label: " [ModelInfo]" while thinking, " [ModelInfo]>" once idle again.
        var bracket = _model.IsThinking ? $"[{_model.ModelInfo}]" : $"[{_model.ModelInfo}]>";
        var modelText = " " + bracket;
        var modelWidth = Math.Min(modelText.Length, Math.Max(0, width - ModelLabelColumn));
        _modelLabel.Text = modelText.Shrink(modelWidth) ?? string.Empty;
        _modelLabel.ForegroundColor = inputFg;
        _modelLabel.BackgroundColor = bg;
        _modelLabel.Position = new Position(ModelLabelColumn, 0);
        _modelLabel.ViewSize = new Size(modelWidth, 1);

        // Prompt label: whatever the user has typed so far, filling the rest of the row — its *end*
        // kept in view once it no longer fits, since that is where the typing is.
        var promptColumn = ModelLabelColumn + modelWidth;
        var promptWidth = Math.Max(0, width - promptColumn);
        _promptLabel.Text = PromptLine.Format(" ", _model.Input, promptWidth);
        _promptLabel.ForegroundColor = inputFg;
        _promptLabel.BackgroundColor = bg;
        _promptLabel.Position = new Position(promptColumn, 0);
        _promptLabel.ViewSize = new Size(promptWidth, 1);

        _aiLabel.Draw(buffer);
        _spinner.Draw(buffer);
        _modelLabel.Draw(buffer);
        _promptLabel.Draw(buffer);

        // Response rows (remaining height)
        if (ViewSize.Height > 1)
        {
            if (!ReferenceEquals(_lastResponseSeen, _model.LastResponse)
                && !_model.LastResponse.StartsWith(_lastResponseSeen, StringComparison.Ordinal))
            {
                // A new answer (not the current one growing as it streams) starts at its top.
                _responseScroll = 0;
            }
            _lastResponseSeen = _model.LastResponse;

            var responseLines = _model.LastResponse.WrapWords(width);
            var rows = ResponseRows;
            _responseScroll = Math.Clamp(_responseScroll, 0, Math.Max(0, responseLines.Count - rows));
            var hiddenBelow = responseLines.Count - _responseScroll - rows;

            for (var row = 1; row < ViewSize.Height; row++)
            {
                var lineIdx = _responseScroll + row - 1;
                var line = (lineIdx < responseLines.Count ? responseLines[lineIdx] : string.Empty)
                    .ExpandRight(width);
                if (row == ViewSize.Height - 1 && hiddenBelow > 0)
                {
                    line = MoreIndicator(line, width, hiddenBelow);
                }
                else if (row == 1 && _responseScroll > 0)
                {
                    line = MoreIndicator(line, width, -_responseScroll);
                }
                buffer.Write(ZOrder,
                    new Position(AbsolutePosition.X, AbsolutePosition.Y + row),
                    new Size(ViewSize.Width, 1),
                    new ConxelString(line, responseFg, bg));
            }
        }
    }

    /// <summary>
    /// Blanks every row of the control in <paramref name="foreground"/>/<paramref name="background"/>
    /// so the composed Label/Spinner pieces (which don't clear their own background when their text
    /// shrinks) never leave stale characters behind from a previous, longer render.
    /// </summary>
    private void BlankRows(IBuffer buffer, Color foreground, Color background, int width)
    {
        var blank = new ConxelString(string.Empty.ExpandRight(width), foreground, background);
        for (var row = 0; row < ViewSize.Height; row++)
        {
            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + row), new Size(ViewSize.Width, 1), blank);
        }
    }

    /// <summary>Overlays "↓ n more (PgDn)" / "↑ n more (PgUp)" on the right end of a response row.</summary>
    internal static string MoreIndicator(string line, int width, int hiddenLines)
    {
        var marker = hiddenLines > 0
            ? $" ↓ {hiddenLines} more (PgDn) "
            : $" ↑ {-hiddenLines} more (PgUp) ";
        if (marker.Length >= width)
        {
            return marker[..Math.Max(0, width)];
        }

        return line[..(width - marker.Length)] + marker;
    }
}

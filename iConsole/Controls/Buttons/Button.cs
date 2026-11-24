using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Controls.Buttons;

public class Button : Control
{
    private ICommand _command { get; set; }

    private string _title = string.Empty;

    /// <summary>
    /// True when the caller named the button. That name is what it shows: the hot key bar builds
    /// "F1-Help", "F2-Terminal", ... and every one of them used to be replaced by the command's own
    /// "Help", "Terminal", ..., so the bar never said which key did what. A button built from a
    /// command alone still follows the command's title, which is what lets a calculator key show
    /// the numeral base it selects.
    /// </summary>
    private readonly bool _hasExplicitTitle;

    public string Title 
    {
        get 
        {
            if (!_hasExplicitTitle && _command is not null)
            {
                var commandTitle = _command.Title;
                if (!string.IsNullOrEmpty(commandTitle))
                {
                    _title = commandTitle;
                }
            }
            return _title;
        }
        set
        {
            // A null title would fail every draw of the button; it is an empty one.
            value ??= string.Empty;
            if (_title != value)
            {
                _title = value;
            }
        }
    }

    /// <summary>How long a press animation runs.</summary>
    public static readonly TimeSpan PressFeedback = TimeSpan.FromMilliseconds(150);

    /// <summary>The press animation, in three steps: pressed colours, then brightened, then the normal colours.</summary>
    private readonly TimedAnimation _press = new((long)PressFeedback.TotalMilliseconds, PressSteps);

    private const int PressSteps = 3;

    /// <summary>The step the button was last drawn at, read and written on the UI loop only. <see cref="PressSteps"/> is the normal state.</summary>
    private int _shownPressStep = PressSteps;

    /// <summary>
    /// The clock the press animation is measured on. The application uses the system clock; a test sets a
    /// <see cref="VirtualTimeProvider"/> and advances it instead of sleeping.
    /// </summary>
    public ITimeProvider Clock { get; set; } = new SystemTimeProvider();

    /// <summary>
    /// True for the length of a press animation: from a click, a Space or a key that runs the button's command, until
    /// <see cref="PressFeedback"/> has passed. The press runs the command as it always has, so there is no held-down state;
    /// the animation is what tells the user the button took the input, most of all for one that closes its dialog a frame later.
    /// </summary>
    public bool IsPressed => _press.Step(Clock.MonotonicMilliseconds) != PressSteps;

    /// <summary>False for a button whose press cannot be painted (the action button draws nothing; its parent draws it).</summary>
    protected virtual bool ShowsPress => true;

    /// <summary>
    /// Starts the press animation, if the button is enabled and paints its press. Called from every path that runs
    /// the button's command: a click, Space, the Ctrl+hot key, and any key bound to the command itself.
    /// Safe to call from the input thread.
    /// </summary>
    public void StartPress()
    {
        if (!IsEnabled || !ShowsPress)
        {
            return;
        }
        _press.Start(Clock.MonotonicMilliseconds);
        StateHasChanged();
    }

    /// <summary>Ends the press animation at once, for a key the button's command did not take.</summary>
    public void StopPress()
    {
        if (!IsPressed)
        {
            return;
        }
        _press.Stop();
        StateHasChanged();
    }

    public override bool IsEnabled
    {
        get
        {
            return _command is not null && _command.CanExecute();
        }
        set => base.IsEnabled = value;
    }

    public Button(string title, ICommand command, IThemeService themeService)
        : base(themeService)
    {
        Title = title;
        _hasExplicitTitle = !string.IsNullOrEmpty(title);
        CanFocus = true;
        PointerShape = PointerShape.Pointer;
        _command = command;
        AddCommand(command);
        AddCommand(new SpaceCommand());
    }

    public Button(ICommand command, IThemeService themeService)
        : base(themeService)
    {
        Title = command.Title;
        CanFocus = true;
        PointerShape = PointerShape.Pointer;
        _command = command;
        AddCommand(command);
        AddCommand(new SpaceCommand());
    }

    /// <summary>
    /// True for a button that deletes, wipes, formats or discards something. It is drawn on the
    /// scheme's error colour, so the user sees at a glance which button cannot be taken back.
    /// </summary>
    public bool IsDestructive { get; set; }

    /// <summary>
    /// True to end a caption the button is too narrow for on an ellipsis ("Select favou…"); false, the default, cuts it at
    /// the edge. The function-key bar relies on the cut, and so does any button sized in cells on purpose.
    /// </summary>
    public bool EllipsisWhenClipped { get; set; }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var visualState = GetVisualState();
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, visualState, out var foregroundColor, out var backgroundColor);
        var hotKeyColor = colorScheme.SelectedFocusedForeground;
        if (IsDestructive && visualState != VisualState.Disabled)
        {
            backgroundColor = colorScheme.Error;
            foregroundColor = visualState == VisualState.Focused ? Color.Yellow : Color.White;
            hotKeyColor = Color.Yellow;
        }
        // A disabled button does not press. Step 0 is the pressed colours; step 1 brightens a normal background
        // (a destructive one keeps its error colour, which has no brighter form in the palette); the end is the normal colours.
        var pressStep = visualState == VisualState.Disabled ? PressSteps : _press.Step(Clock.MonotonicMilliseconds);
        if (pressStep == 0)
        {
            foregroundColor = colorScheme.SelectedFocusedForeground;
            backgroundColor = colorScheme.SelectedFocusedBackground;
            hotKeyColor = colorScheme.SelectedFocusedForeground;
        }
        else if (pressStep == 1 && !IsDestructive)
        {
            backgroundColor = Brighten(backgroundColor);
        }
        var inner = Math.Max(0, (int)ViewSize.Width - 2);
        var title = inner == 0
            ? string.Empty
            : EllipsisWhenClipped && inner > 1 && Title.Length > inner ? (Title[..(inner - 1)] + "…").Expand(inner) : Title.Cut(inner).Expand(inner);
        var titleLine = new ConxelString(Border.Start + title + Border.End, foregroundColor, backgroundColor);
        titleLine.HighlightHotKey(HotConsoleKey, HotKeyChar, hotKeyColor, backgroundColor);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, titleLine);
    }


    public override void OnIdle()
    {
        base.OnIdle();
        // Repaint only when the press moves to another step, or ends, so an idle button costs nothing.
        var step = _press.Step(Clock.MonotonicMilliseconds);
        if (step != _shownPressStep)
        {
            _shownPressStep = step;
            StateHasChanged();
        }
    }

    /// <summary>The background of the brightened press step: each dark colour becomes its bright counterpart.</summary>
    private static Color Brighten(Color color) => color switch
    {
        Color.DarkBlue => Color.Blue,
        Color.DarkGreen => Color.Green,
        Color.DarkCyan => Color.Cyan,
        Color.DarkRed => Color.Red,
        Color.DarkMagenta => Color.Magenta,
        Color.DarkYellow => Color.Yellow,
        Color.DarkGray => Color.Gray,
        _ => color,
    };

    public override void GetColor(ColorScheme colorScheme, VisualState visualState, out Color foreground, out Color background)
    {
        switch (visualState)
        {
            case VisualState.Normal:
            default:
                {
                    foreground = colorScheme.Foreground;
                    background = colorScheme.ButtonBackground;
                    break;
                }
            case VisualState.Disabled:
                {
                    foreground = colorScheme.DisabledForeground;
                    background = colorScheme.DisabledButtonBackground;
                    break;
                }
            case VisualState.Focused:
                {
                    foreground = colorScheme.SelectedFocusedForeground;
                    background = colorScheme.ButtonBackground;
                    break;
                }
        }
    }

    private async Task<bool> RunFirstCommandAsync()
    {
        ICommand? command = Commands.FirstOrDefault();
        return command is not null && await command.RunAsync(null);
    }

    public override async Task TriggerHotKeyAsync()
    {
        await base.TriggerHotKeyAsync();
        StartPress();
        if (!await RunFirstCommandAsync())
        {
            StopPress();
        }
    }

    /// <summary>
    /// A key bound to the button's own command (a function key on the key bar, Enter on a close button) runs it through
    /// the command, not through <see cref="RunFirstCommandAsync"/>, so the press starts here. A key the command declines
    /// (its <c>RunAsync</c> returns false, for example a copy with nothing to copy) ends the press at once.
    /// Space is handled by <see cref="SpaceCommand"/>, which is the button's second command.
    /// </summary>
    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var takesKey = IsEnabled && (Commands.FirstOrDefault()?.ActiveKey?.IsIdentical(keyEvent) ?? false);
        if (takesKey)
        {
            StartPress();
        }
        var isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (takesKey && !isHandled)
        {
            StopPress();
        }
        return isHandled;
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled)
        {
            yield break;
        }

        yield return ("Space", UiText.Get("Hint.PressButton", "Press the button"));
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        var isHandled = await base.HandleMouseEventAsync(mouseEvent);

        // Only the left button presses a button. The right one is the context-menu button
        // everywhere else, and an accidental right-click on "Delete" must not run it.
        if (AbsoluteBoundRect.Contains(mouseEvent.Position)
            && mouseEvent.WheelDelta == 0
            && mouseEvent.LeftClicked)
        {
            if (IsEnabled)
            {
                // A clicked button takes the focus, as a clicked list or text box does, so Enter
                // after the click reaches the button the user just pressed.
                if (!IsFocused)
                {
                    SetFocus();
                }

                StartPress();
            }
            if (!await RunFirstCommandAsync())
            {
                StopPress();
            }
            isHandled = true;
        }
        return isHandled;
    }
}

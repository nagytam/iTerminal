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
        private set
        {
            if (_title != value)
            {
                _title = value;
            }
        }
    }

    /// <summary>How long a click leaves the button drawn as pressed.</summary>
    public static readonly TimeSpan PressFeedback = TimeSpan.FromMilliseconds(150);

    private long _pressedAt;

    /// <summary>
    /// True for a moment after a mouse click. A click runs the command on the press, as it always
    /// has, so there is no held-down state to show; the flash is what tells the user the button
    /// took the click — most of all for one that closes the dialog it is in a frame later.
    /// </summary>
    public bool IsPressed => Volatile.Read(ref _pressedAt) != 0;

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
        _command = command;
        AddCommand(command);
        AddCommand(new SpaceCommand());
    }

    public Button(ICommand command, IThemeService themeService)
        : base(themeService)
    {
        Title = command.Title;
        CanFocus = true;
        _command = command;
        AddCommand(command);
        AddCommand(new SpaceCommand());
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var visualState = GetVisualState();
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, visualState, out var foregroundColor, out var backgroundColor);
        if (IsPressed && visualState != VisualState.Disabled)
        {
            foregroundColor = colorScheme.SelectedFocusedForeground;
            backgroundColor = colorScheme.SelectedFocusedBackground;
        }
        var inner = Math.Max(0, (int)ViewSize.Width - 2);
        var title = inner == 0
            ? string.Empty
            : Title.Cut(inner).Expand(inner);
        var titleLine = new ConxelString(Border.Start + title + Border.End, foregroundColor, backgroundColor);
        titleLine.HighlightHotKey(HotConsoleKey, HotKeyChar, colorScheme.SelectedFocusedForeground, backgroundColor);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, titleLine);
    }


    public override void OnIdle()
    {
        base.OnIdle();
        var pressedAt = Volatile.Read(ref _pressedAt);
        if (pressedAt != 0 && Environment.TickCount64 - pressedAt >= PressFeedback.TotalMilliseconds)
        {
            Volatile.Write(ref _pressedAt, 0);
            StateHasChanged();
        }
    }

    public override void GetColor(ColorScheme colorScheme, VisualState visualState, out Color foregroundColor, out Color backgroundColor)
    {
        switch (visualState)
        {
            case VisualState.Normal:
            default:
                {
                    foregroundColor = colorScheme.Foreground;
                    backgroundColor = colorScheme.ButtonBackground;
                    break;
                }
            case VisualState.Disabled:
                {
                    foregroundColor = colorScheme.DisabledForeground;
                    backgroundColor = colorScheme.DisabledButtonBackground;
                    break;
                }
            case VisualState.Focused:
                {
                    foregroundColor = colorScheme.SelectedFocusedForeground;
                    backgroundColor = colorScheme.ButtonBackground;
                    break;
                }
        }
    }

    private async Task RunFirstCommandAsync()
    {
        ICommand? command = Commands.FirstOrDefault();
        if (command is not null)
        {
            await command.RunAsync(null);
        }
    }

    public override async Task TriggerHotKeyAsync()
    {
        await base.TriggerHotKeyAsync();
        await RunFirstCommandAsync();
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

                Volatile.Write(ref _pressedAt, Math.Max(1, Environment.TickCount64));
                StateHasChanged();
            }
            await RunFirstCommandAsync();
            isHandled = true;
        }
        return isHandled;
    }
}

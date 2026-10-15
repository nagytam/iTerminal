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
        var inner = Math.Max(0, (int)ViewSize.Width - 2);
        var title = inner == 0
            ? string.Empty
            : Title.Cut(inner).Expand(inner);
        var titleLine = new ConxelString(Border.Start + title + Border.End, foregroundColor, backgroundColor);
        titleLine.HighlightHotKey(HotConsoleKey, HotKeyChar, colorScheme.SelectedFocusedForeground, backgroundColor);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, titleLine);
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

    public override async Task TriggerHotKeyAsync()
    {
        await base.TriggerHotKeyAsync();
        await Commands.FirstOrDefault()?.RunAsync(null);
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        var isHandled = await base.HandleMouseEventAsync(mouseEvent);

        if (AbsoluteBoundRect.Contains(mouseEvent.Position)
            && mouseEvent.WheelDelta == 0
            && (mouseEvent.LeftClicked || mouseEvent.RightClicked))
        {
            await Commands.FirstOrDefault()?.RunAsync(null);
            isHandled = true;
        }
        return isHandled;
    }
}

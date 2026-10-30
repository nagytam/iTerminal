using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

public class CheckBox : Control, IReadOnly
{
    private bool _isChecked;
    public bool IsChecked
    {
        get
        {
            return _isChecked;
        }
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                CheckedChanged?.Invoke(this, EventArgs.Empty);
                StateHasChanged();
            }
        }
    }

    public bool IsReadOnly { get; set; }
    public string Label { get; set; }
    private string CheckedText { get; set; }
    private string UncheckedText { get; set; }
    public event EventHandler? CheckedChanged;

    public CheckBox(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        IsChecked = false;
        Label = string.Empty;
        CheckedText = "▣ ";
        UncheckedText = "□ ";
        ViewSize = new Size(10, 1);

        AddCommand(new ToggleCheckBoxCommand());
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (IsReadOnly || !IsEnabled)
        {
            yield break;
        }

        yield return ("Space", UiText.Get("Hint.CheckOrUncheck", "Check or uncheck"));
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        var isHandled = await base.HandleMouseEventAsync(mouseEvent);

        // Mirror Button: a left click anywhere on the control toggles it, so the checkbox is operable
        // by mouse as well as the Space key. The right button is left alone, as on Button, so a
        // stray right-click cannot flip a setting. Read-only checkboxes stay inert, matching the command.
        if (!IsReadOnly
            && IsEnabled
            && AbsoluteBoundRect.Contains(mouseEvent.Position)
            && mouseEvent.WheelDelta == 0
            && mouseEvent.LeftClicked)
        {
            // Take the focus with the click, as the keyboard Space would need it: otherwise the box
            // flips while the focus stays on another control, and the next key goes elsewhere.
            if (!IsFocused)
            {
                SetFocus();
            }

            IsChecked = !IsChecked;
            isHandled = true;
        }

        return isHandled;
    }

    public override void Draw(IBuffer buffer)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        var line = IsChecked ? CheckedText : UncheckedText;
        line += Label;
        line = line.PadRight((int)ViewSize.Width);
        // Read-only draws in the disabled colours, as RadioButton does: a box that looks live but
        // ignores Space reads as broken. It is not struck through — the value is still meaningful.
        var visualState = IsReadOnly && IsEnabled ? VisualState.Disabled : GetVisualState();
        GetColor(colorScheme, visualState, out Color foreground, out Color background);
        var isStrikeThrough = !IsEnabled;
        var lineString = new ConxelString(line, foreground, background, isStrikeThrough: isStrikeThrough);
        // The hot key is shown in the label, as on TextBox and ComboBox: without the underline a
        // checkbox answers its hot key with nothing on screen to say which key that is.
        lineString.HighlightHotKey(HotConsoleKey, HotKeyChar, ThemeService.CurrentColorScheme.SelectedFocusedForeground, background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, lineString);
    }

    public override void GetColor(ColorScheme colorScheme, VisualState visualState, out Color foregroundColor, out Color backgroundColor)
    {
        switch (visualState)
        {
            case VisualState.Normal:
            default:
                {
                    foregroundColor = colorScheme.Foreground;
                    backgroundColor = colorScheme.Background;
                    break;
                }
            case VisualState.Focused:
                {
                    foregroundColor = colorScheme.SelectedFocusedForeground;
                    backgroundColor = colorScheme.Background;
                    break;
                }
            case VisualState.Disabled:
                {
                    foregroundColor = colorScheme.DisabledForeground;
                    backgroundColor = colorScheme.DisabledBackground;
                    break;
                }
            case VisualState.Selected:
                {
                    foregroundColor = colorScheme.SelectedForeground;
                    backgroundColor = colorScheme.SelectedBackground;
                    break;
                }
            case VisualState.SelectedDisabled:
                {
                    foregroundColor = colorScheme.DisabledForeground;
                    backgroundColor = colorScheme.DisabledBackground;
                    break;
                }
        }
    }
}

using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// Focusable two-state button that renders as <c>[ ON ]</c> / <c>[OFF]</c>.
/// Space or Enter toggles the state; a left click anywhere on the control does the same.
/// </summary>
public class ToggleButton : Control, IReadOnly
{
    private bool _isOn;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn != value)
            {
                _isOn = value;
                Toggled?.Invoke(this, _isOn);
                StateHasChanged();
            }
        }
    }

    public bool IsReadOnly { get; set; }

    /// <summary>Text shown inside the brackets when <see cref="IsOn"/> is <c>true</c>.</summary>
    public string LabelOn
    {
        get => _labelOn;
        set => _labelOn = value ?? string.Empty;
    }

    /// <summary>Text shown inside the brackets when <see cref="IsOn"/> is <c>false</c>.</summary>
    public string LabelOff
    {
        get => _labelOff;
        set => _labelOff = value ?? string.Empty;
    }

    private string _labelOn = "ON";
    private string _labelOff = "OFF";

    /// <summary>Fired after each state change; the argument is the new <see cref="IsOn"/> value.</summary>
    public event EventHandler<bool>? Toggled;

    public ToggleButton(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        AddCommand(new ToggleCommand(new Key(ConsoleKey.Spacebar, false, false, false)));
        AddCommand(new ToggleCommand(new Key(ConsoleKey.Enter, false, false, false)));
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (IsReadOnly || !IsEnabled)
        {
            yield break;
        }

        yield return ("Space/Enter", "Switch the setting on or off");
    }

    private void Toggle()
    {
        IsOn = !IsOn;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        var handled = await base.HandleMouseEventAsync(mouseEvent);
        if (!IsReadOnly
            && IsEnabled
            && AbsoluteBoundRect.Contains(mouseEvent.Position)
            && mouseEvent.WheelDelta == 0
            && mouseEvent.LeftClicked)
        {
            if (!IsFocused)
            {
                SetFocus();
            }

            Toggle();
            handled = true;
        }
        return handled;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        var visualState = IsReadOnly && IsEnabled ? VisualState.Disabled : GetVisualState();
        GetColor(colorScheme, visualState, out var fg, out var bg);

        var label = IsOn ? LabelOn : LabelOff;
        var inner = Math.Max(0, (int)ViewSize.Width - 2);
        var title = label.Cut(inner).Expand(inner);
        var line = new ConxelString(Border.Start + title + Border.End, fg, bg);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }

    public override void GetColor(ColorScheme colorScheme, VisualState visualState,
        out Color foregroundColor, out Color backgroundColor)
    {
        switch (visualState)
        {
            case VisualState.Focused:
                // Focus changes the text colour only; the background still says ON or OFF, so the
                // state stays readable on the one control the user is looking at.
                foregroundColor = colorScheme.SelectedFocusedForeground;
                backgroundColor = IsOn ? colorScheme.SelectedBackground : colorScheme.ButtonBackground;
                break;
            case VisualState.Disabled:
                foregroundColor = colorScheme.DisabledForeground;
                backgroundColor = colorScheme.DisabledButtonBackground;
                break;
            default:
                // Use selection colours when ON to make the active state visually distinct.
                if (IsOn)
                {
                    foregroundColor = colorScheme.SelectedForeground;
                    backgroundColor = colorScheme.SelectedBackground;
                }
                else
                {
                    foregroundColor = colorScheme.Foreground;
                    backgroundColor = colorScheme.ButtonBackground;
                }
                break;
        }
    }
}

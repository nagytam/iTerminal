using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A single selectable option rendered as <c>◉ Label</c> (selected) or <c>○ Label</c>
/// (unselected). It is the radio counterpart of <see cref="CheckBox"/> and shares its glyph + label
/// + read-only conventions, but — like a real radio — a user action only ever *selects* it; it never
/// clears itself. Exclusivity is owned by <see cref="RadioGroup{T}"/>, which listens to
/// <see cref="SelectRequested"/>. A standalone <see cref="RadioButton"/> is fully usable on its own.
/// </summary>
public class RadioButton : Control, IReadOnly
{
    private const string SelectedGlyph = "◉ ";
    private const string UnselectedGlyph = "○ ";

    private bool _isSelected;

    public RadioButton(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(12, 1);
    }

    public string Label { get; set; } = string.Empty;
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// Set by an owning <see cref="RadioGroup{T}"/> so the selected option renders with the focused
    /// look while the group has keyboard focus, independent of this control's own focus flag.
    /// </summary>
    public bool IsHighlighted { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                SelectedChanged?.Invoke(this, EventArgs.Empty);
                StateHasChanged();
            }
        }
    }

    /// <summary>Raised when <see cref="IsSelected"/> changes.</summary>
    public event EventHandler? SelectedChanged;

    /// <summary>Raised when the user picks this option (click or Space), before exclusivity is applied.</summary>
    public event EventHandler? SelectRequested;

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var isHandled = await base.HandleKeyEventAsync(keyEvent);

        // Guarding on IsFocused matters inside a RadioGroup: the group dispatches keys to all of its
        // (non-focusable) children, and only the focused control should react to Space.
        if (isHandled || IsReadOnly || !IsFocused)
        {
            return isHandled;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Spacebar)
        {
            Select();
            return true;
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        var isHandled = await base.HandleMouseEventAsync(mouseEvent);

        if (!IsReadOnly
            && IsEnabled
            && AbsoluteBoundRect.Contains(mouseEvent.Position)
            && mouseEvent.WheelDelta == 0
            && mouseEvent.LeftClicked)
        {
            Select();
            isHandled = true;
        }

        return isHandled;
    }

    private void Select()
    {
        IsSelected = true;
        SelectRequested?.Invoke(this, EventArgs.Empty);
    }

    public override void Draw(IBuffer buffer)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        var line = (IsSelected ? SelectedGlyph : UnselectedGlyph) + Label;
        line = line.PadRight((int)ViewSize.Width);

        var visualState = !IsEnabled || IsReadOnly
            ? VisualState.Disabled
            : IsFocused || IsHighlighted ? VisualState.Focused : VisualState.Normal;
        GetColor(colorScheme, visualState, out var foreground, out var background);

        var lineString = new ConxelString(line, foreground, background, isStrikeThrough: !IsEnabled);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, lineString);
    }

    public override void GetColor(ColorScheme colorScheme, VisualState visualState, out Color foregroundColor, out Color backgroundColor)
    {
        switch (visualState)
        {
            case VisualState.Focused:
                foregroundColor = colorScheme.SelectedFocusedForeground;
                backgroundColor = colorScheme.Background;
                break;
            case VisualState.Disabled:
                foregroundColor = colorScheme.DisabledForeground;
                backgroundColor = colorScheme.DisabledBackground;
                break;
            case VisualState.Selected:
                foregroundColor = colorScheme.SelectedForeground;
                backgroundColor = colorScheme.SelectedBackground;
                break;
            case VisualState.SelectedDisabled:
                foregroundColor = colorScheme.DisabledForeground;
                backgroundColor = colorScheme.DisabledBackground;
                break;
            case VisualState.Normal:
            default:
                foregroundColor = colorScheme.Foreground;
                backgroundColor = colorScheme.Background;
                break;
        }
    }
}

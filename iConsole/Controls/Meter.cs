using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// Horizontal level bar for a continuous 0–1 value.
/// Unlike ProgressBar, Meter is not tied to a BackgroundTask and carries no percentage overlay.
/// </summary>
public class Meter(IThemeService themeService) : Control(themeService)
{
    private double _value = 0.0;

    /// <summary>Current fill level, clamped to [0, 1] on set.</summary>
    public double Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (_value != clamped)
            {
                _value = clamped;
                StateHasChanged();
            }
        }
    }

    /// <summary>Character used for the filled portion of the bar.</summary>
    public char FillChar { get; set; } = '█';

    /// <summary>Character used for the empty portion of the bar.</summary>
    public char EmptyChar { get; set; } = '░';

    /// <summary>Optional label shown to the left of the bar, separated by a space.</summary>
    public string Label { get; set; } = string.Empty;

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var fg, out var bg);

        var barWidth = (int)ViewSize.Width;
        var prefix = string.Empty;
        if (!string.IsNullOrEmpty(Label))
        {
            prefix = Label + " ";
            barWidth = Math.Max(0, barWidth - prefix.Length);
        }

        var filled = (int)(barWidth * _value);
        var empty = barWidth - filled;
        var bar = StringHelper.Repeat(FillChar, filled) + StringHelper.Repeat(EmptyChar, empty);
        var line = prefix + bar;

        buffer.Write(ZOrder, AbsolutePosition, ViewSize, new ConxelString(line, fg, bg));
    }

    public override void GetColor(ColorScheme colorScheme, VisualState visualState, out Color foregroundColor, out Color backgroundColor)
    {
        switch (visualState)
        {
            case VisualState.Disabled:
                foregroundColor = colorScheme.DisabledForeground;
                backgroundColor = colorScheme.DisabledBackground;
                break;
            case VisualState.Focused:
            case VisualState.Selected:
                foregroundColor = colorScheme.SelectedFocusedForeground;
                backgroundColor = colorScheme.SelectedFocusedBackground;
                break;
            default:
                foregroundColor = colorScheme.Foreground;
                backgroundColor = colorScheme.Background;
                break;
        }
    }
}

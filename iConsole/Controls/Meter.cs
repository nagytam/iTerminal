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
    private const int MinBarWidth = 4;

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

    /// <summary>Overlays the value as a percentage (<c> 42% </c>) centred on the bar, so the number can be read, not counted.</summary>
    public bool ShowPercent { get; set; }

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
            // The level is what the control is for: a long label is cut so the bar keeps at least a few cells.
            string label = Label.Cut(Math.Max(0, barWidth - MinBarWidth - 1)).TrimEnd();
            prefix = label.Length == 0 ? string.Empty : label + " ";
            barWidth -= prefix.Length;
        }

        var filled = (int)(barWidth * _value);
        var empty = barWidth - filled;
        var bar = StringHelper.Repeat(FillChar, filled) + StringHelper.Repeat(EmptyChar, empty);
        if (ShowPercent && barWidth >= 6)
        {
            bar = StringHelper.AlignCenter(bar, $" {(int)Math.Round(_value * 100)}% ");
        }
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

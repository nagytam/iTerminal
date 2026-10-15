using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Controls.Progress;

public class ProgressBar : Control
{
    public string Label { get; set; } = "Progress";
    private int _current = 0;
    private readonly Lock _lock = new();
    public int Current 
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
        set
        {
            lock (_lock)
            {
                if (_current != value)
                {
                    _current = value;
                }
                StateHasChanged();
            }
        }
    } 
    public int Min { get; set; } = 0;
    public int Max { get; set; } = 100;

    public ProgressBar(IThemeService themeService) : base(themeService)
    {
        ViewSize = new Data.Size(0, 1);
    }

    /// <summary>
    /// How far <see cref="Current"/> is through [<see cref="Min"/>, <see cref="Max"/>], clamped to
    /// [0, 1]. A value outside the range draws as empty or full — returning without drawing, as the
    /// bar once did, left the previous frame on screen, so an overshooting task froze its bar at
    /// whatever it last showed. An empty or inverted range reads as 0 rather than dividing by zero.
    /// </summary>
    public double Fraction
    {
        get
        {
            var range = (double)Max - Min;
            if (range <= 0)
            {
                return 0;
            }
            return Math.Clamp((Current - (double)Min) / range, 0, 1);
        }
    }

    public override void Draw(IBuffer buffer)
    {
        if (ViewSize.IsEmpty)
        {
            return;
        }

        var fraction = Fraction;
        var prefix = TextBox.FormatLabelPrefix(Label);
        var barWidth = Math.Max(0, (int)ViewSize.Width - prefix.Length - 2);

        // The empty part is what is left of the bar, not a second rounding of the same product:
        // truncating both halves independently made the bar a cell short at most fractions, and
        // the closing bracket moved back and forth as the task progressed.
        var filled = (int)(barWidth * fraction);
        var bar = "▬".Repeat(filled) + "‧".Repeat(barWidth - filled);
        // Centred on the bar, not on the whole line: centred on the line it sat as far left of
        // the bar's middle as the label is long.
        bar = StringHelper.AlignCenter(bar, $" {(int)(fraction * 100)}% ");
        var line = prefix + Border.Start + bar + Border.End;
        line = line.Cut((int)ViewSize.Width).PadRight((int)ViewSize.Width);
        var visualState = GetVisualState();
        GetColor(ThemeService.CurrentColorScheme, visualState, out var foreground, out var background);
        var lineString = new ConxelString(line, foreground, background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, lineString);
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
                backgroundColor = colorScheme.SelectedBackground;
                break;
            default:
                foregroundColor = colorScheme.Foreground;
                backgroundColor = colorScheme.Background;
                break;
        }
    }
}

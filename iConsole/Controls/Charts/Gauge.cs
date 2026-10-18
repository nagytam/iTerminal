using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls.Charts;

/// <summary>
/// A labelled level bar whose colour follows how full it is: green below <see cref="WarningThreshold"/>,
/// yellow from there to <see cref="CriticalThreshold"/>, red beyond. Good for "how much of this is used"
/// (disk, memory) where a full bar is bad news. The percentage is drawn at the right end.
/// </summary>
public class Gauge(IThemeService themeService) : Control(themeService)
{
    private double _value;

    /// <summary>Current fill level, clamped to [0, 1].</summary>
    public double Value
    {
        get => _value;
        set
        {
            double clamped = double.IsNaN(value) ? 0 : Math.Clamp(value, 0.0, 1.0);
            if (_value != clamped)
            {
                _value = clamped;
                StateHasChanged();
            }
        }
    }

    /// <summary>Text drawn left of the bar.</summary>
    public string Label { get; set; } = string.Empty;

    public double WarningThreshold { get; set; } = 0.75;

    public double CriticalThreshold { get; set; } = 0.9;

    /// <summary>Colour for <paramref name="ratio"/>: green, yellow or red.</summary>
    public static Color SeverityColor(double ratio, double warning = 0.75, double critical = 0.9) =>
        ratio >= critical ? Color.Red : ratio >= warning ? Color.Yellow : Color.Green;

    /// <summary>Bar of <paramref name="width"/> cells (1/8-cell resolution), padded with the light shade.</summary>
    public static string BuildBar(double ratio, int width) =>
        BarChart.BuildBar(double.IsNaN(ratio) ? 0 : ratio, width).Replace(' ', '░');

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        string prefix = Label.Length == 0 ? string.Empty : Label + " ";
        string percent = $" {_value * 100,3:0}%";
        int barWidth = Math.Max(0, width - prefix.Length - percent.Length);
        Color barColor = SeverityColor(_value, WarningThreshold, CriticalThreshold);
        long x = AbsolutePosition.X;
        long y = AbsolutePosition.Y;
        if (barWidth == 0)
        {
            string only = (prefix + percent).PadRight(width);
            buffer.Write(ZOrder, AbsolutePosition, ViewSize, new ConxelString(only[..width], colorScheme.Foreground, colorScheme.Background));
            return;
        }

        buffer.Write(ZOrder, new Position(x, y), new Size(prefix.Length, 1), new ConxelString(prefix, colorScheme.Foreground, colorScheme.Background));
        buffer.Write(ZOrder, new Position(x + prefix.Length, y), new Size(barWidth, 1), new ConxelString(BuildBar(_value, barWidth), barColor, colorScheme.Background));
        buffer.Write(ZOrder, new Position(x + prefix.Length + barWidth, y), new Size(percent.Length, 1), new ConxelString(percent, colorScheme.Foreground, colorScheme.Background));
    }
}

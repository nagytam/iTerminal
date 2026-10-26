using System.Globalization;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls.Charts;

/// <summary>One bar of a <see cref="BarChart"/>.</summary>
public record BarChartItem(string Label, double Value, Color? Color = null);

/// <summary>
/// Horizontal bar chart: one row per item with an aligned label, a bar with 1/8-cell resolution and the value.
/// </summary>
public class BarChart(IThemeService themeService) : Control(themeService)
{
    private static readonly char[] Partials = [' ', '▏', '▎', '▍', '▌', '▋', '▊', '▉'];

    private IReadOnlyList<BarChartItem> _items = [];

    /// <summary>The bars, drawn top to bottom. Rows beyond the control height are clipped.</summary>
    public IReadOnlyList<BarChartItem> Items
    {
        get => _items;
        set
        {
            _items = value ?? [];
            StateHasChanged();
        }
    }

    /// <summary>Value that fills the whole bar. Null scales to the largest item.</summary>
    public double? MaxValue { get; set; }

    /// <summary>Appends each item's value after its bar.</summary>
    public bool ShowValues { get; set; } = true;

    /// <summary>Format string for the values.</summary>
    public string ValueFormat
    {
        get => _valueFormat;
        set => _valueFormat = string.IsNullOrEmpty(value) ? DefaultValueFormat : value;
    }

    private const string DefaultValueFormat = "0.##";
    private string _valueFormat = DefaultValueFormat;

    /// <summary>The value as text; a format string the number cannot use falls back to the default instead of failing every draw.</summary>
    private string FormatValue(double value)
    {
        try
        {
            return value.ToString(_valueFormat, CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            return value.ToString(DefaultValueFormat, CultureInfo.CurrentCulture);
        }
    }

    /// <summary>Colour for items that do not specify one. Null uses the theme foreground.</summary>
    public Color? DefaultBarColor { get; set; }

    /// <summary>Builds the bar glyphs for a ratio in [0, 1] over <paramref name="width"/> cells.</summary>
    public static string BuildBar(double ratio, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        // NaN has no bar: clamping leaves it NaN and the cast to int then gives a negative length.
        int eighths = double.IsNaN(ratio) ? 0 : (int)Math.Round(Math.Clamp(ratio, 0.0, 1.0) * width * 8);
        int full = eighths / 8;
        int rest = eighths % 8;
        string bar = new string('█', full) + (rest > 0 ? Partials[rest].ToString() : string.Empty);
        return bar.PadRight(width);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        int height = (int)ViewSize.Height;
        Color barDefault = DefaultBarColor ?? colorScheme.Foreground;

        int labelWidth = _items.Count == 0 ? 0 : _items.Max(i => (i.Label ?? string.Empty).Length);
        double max = MaxValue ?? (_items.Count == 0 ? 0 : _items.Where(i => !double.IsNaN(i.Value)).Select(i => i.Value).DefaultIfEmpty(0).Max());
        string[] values = _items.Select(i => ShowValues ? FormatValue(i.Value) : string.Empty).ToArray();
        int valueWidth = values.Length == 0 ? 0 : values.Max(v => v.Length);
        int valueColumn = ShowValues && valueWidth > 0 ? valueWidth + 1 : 0;
        int barWidth = Math.Max(0, width - labelWidth - 1 - valueColumn);

        for (int row = 0; row < height; row++)
        {
            Position rowPosition = new(AbsolutePosition.X, AbsolutePosition.Y + row);
            Size rowSize = new(width, 1);

            if (row >= _items.Count)
            {
                buffer.Write(ZOrder, rowPosition, rowSize, new ConxelString(new string(' ', width), colorScheme.Foreground, colorScheme.Background));
                continue;
            }

            BarChartItem item = _items[row];
            double ratio = max <= 0 ? 0 : item.Value / max;
            string label = (item.Label ?? string.Empty).PadRight(labelWidth) + " ";
            string bar = BuildBar(ratio, barWidth);
            string value = valueColumn > 0 ? " " + values[row].PadLeft(valueWidth) : string.Empty;

            long x = rowPosition.X;
            buffer.Write(ZOrder, new Position(x, rowPosition.Y), new Size(Math.Min(label.Length, width), 1), new ConxelString(label.Length > width ? label[..width] : label, colorScheme.Foreground, colorScheme.Background));
            x += label.Length;
            if (barWidth > 0)
            {
                buffer.Write(ZOrder, new Position(x, rowPosition.Y), new Size(barWidth, 1), new ConxelString(bar, item.Color ?? barDefault, colorScheme.Background));
                x += barWidth;
            }

            long remaining = rowPosition.X + width - x;
            if (remaining > 0)
            {
                string tail = value.PadRight((int)remaining);
                buffer.Write(ZOrder, new Position(x, rowPosition.Y), new Size(remaining, 1), new ConxelString(tail[..(int)remaining], colorScheme.Foreground, colorScheme.Background));
            }
        }
    }
}

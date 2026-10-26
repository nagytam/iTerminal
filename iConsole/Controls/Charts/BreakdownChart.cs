using System.Globalization;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls.Charts;

/// <summary>One slice of a <see cref="BreakdownChart"/>.</summary>
public record BreakdownChartItem(string Label, double Value, Color Color);

/// <summary>
/// Single stacked bar split proportionally between its items, followed by a colour-keyed legend
/// (one entry per row) when the control is taller than one row.
/// </summary>
public class BreakdownChart(IThemeService themeService) : Control(themeService)
{
    private IReadOnlyList<BreakdownChartItem> _items = [];

    /// <summary>The slices. Items with a non-positive value are ignored.</summary>
    public IReadOnlyList<BreakdownChartItem> Items
    {
        get => _items;
        set
        {
            _items = value ?? [];
            StateHasChanged();
        }
    }

    /// <summary>Shows each legend entry's share as a percentage.</summary>
    public bool ShowPercentages { get; set; } = true;

    /// <summary>
    /// Splits <paramref name="width"/> cells between the values with the largest-remainder method,
    /// so the widths always add up to the full width (when any value is positive).
    /// </summary>
    /// <summary>What a value counts for: only a finite positive one has a share, so NaN and infinity cannot spoil the others' widths.</summary>
    private static double Weight(double value) => double.IsFinite(value) && value > 0 ? value : 0;

    public static int[] Allocate(IReadOnlyList<double> values, int width)
    {
        int[] widths = new int[values.Count];
        double total = values.Sum(Weight);
        if (total <= 0 || width <= 0)
        {
            return widths;
        }

        double[] remainders = new double[values.Count];
        int used = 0;
        for (int i = 0; i < values.Count; i++)
        {
            double exact = Weight(values[i]) / total * width;
            widths[i] = (int)Math.Floor(exact);
            remainders[i] = exact - widths[i];
            used += widths[i];
        }

        foreach (int i in Enumerable.Range(0, values.Count).OrderByDescending(i => remainders[i]).Take(width - used))
        {
            widths[i]++;
        }

        return widths;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        int height = (int)ViewSize.Height;
        double total = _items.Sum(i => Weight(i.Value));
        int[] widths = Allocate(_items.Select(i => i.Value).ToList(), width);

        long x = AbsolutePosition.X;
        long y = AbsolutePosition.Y;
        for (int i = 0; i < _items.Count; i++)
        {
            if (widths[i] > 0)
            {
                buffer.Write(ZOrder, new Position(x, y), new Size(widths[i], 1), new ConxelString(new string('█', widths[i]), _items[i].Color, colorScheme.Background));
                x += widths[i];
            }
        }

        long barEnd = AbsolutePosition.X + width;
        if (x < barEnd)
        {
            buffer.Write(ZOrder, new Position(x, y), new Size(barEnd - x, 1), new ConxelString(new string(' ', (int)(barEnd - x)), colorScheme.Foreground, colorScheme.Background));
        }

        for (int row = 1; row < height; row++)
        {
            Position rowPosition = new(AbsolutePosition.X, AbsolutePosition.Y + row);
            int index = row - 1;
            if (index >= _items.Count)
            {
                buffer.Write(ZOrder, rowPosition, new Size(width, 1), new ConxelString(new string(' ', width), colorScheme.Foreground, colorScheme.Background));
                continue;
            }

            BreakdownChartItem item = _items[index];
            string value = item.Value.ToString("0.##", CultureInfo.CurrentCulture);
            string share = ShowPercentages && total > 0 && Weight(item.Value) > 0
                ? $" ({item.Value / total * 100:0.#}%)"
                : string.Empty;
            string text = $" {item.Label} {value}{share}";
            if (width < 2)
            {
                if (width == 1)
                {
                    buffer.Write(ZOrder, rowPosition, new Size(1, 1), new ConxelString(" ", colorScheme.Foreground, colorScheme.Background));
                }

                continue;
            }

            string line = text.Length > width - 1 ? text[..(width - 1)] : text.PadRight(width - 1);
            buffer.Write(ZOrder, rowPosition, new Size(1, 1), new ConxelString("■", item.Color, colorScheme.Background));
            buffer.Write(ZOrder, new Position(rowPosition.X + 1, rowPosition.Y), new Size(width - 1, 1), new ConxelString(line, colorScheme.Foreground, colorScheme.Background));
        }
    }
}

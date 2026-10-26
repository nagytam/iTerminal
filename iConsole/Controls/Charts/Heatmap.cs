using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Controls.Charts;

/// <summary>
/// A grid of cells shaded by value (0–1), for patterns over two dimensions — activity by weekday and hour,
/// usage by drive and day. Each cell is two columns wide: a shade (<c>· ░ ▒ ▓ █</c>) and a colour that run from
/// quiet to hot together, so the picture still reads in a terminal that cannot show colour. Optional row labels
/// sit on the left and column labels above. Every row of the control is painted, so a smaller data set leaves
/// nothing of the old one behind.
/// </summary>
public class Heatmap : Control
{
    /// <summary>The shades, quietest first.</summary>
    public static readonly char[] Shades = ['·', '░', '▒', '▓', '█'];

    private static readonly Color[] Ramp = [Color.DarkGray, Color.DarkCyan, Color.Green, Color.Yellow, Color.Red];

    private double[][] _values = [];
    private string[] _rowLabels = [];
    private string[] _columnLabels = [];

    public Heatmap(IThemeService themeService) : base(themeService)
    {
        ViewSize = new Size(30, 5);
    }

    /// <summary>Rows of values, each clamped to [0, 1] when drawn; rows may differ in length.</summary>
    public IReadOnlyList<IReadOnlyList<double>> Values
    {
        get => _values;
        set
        {
            _values = [.. (value ?? []).Select(row => row?.ToArray() ?? [])];
            StateHasChanged();
        }
    }

    public IReadOnlyList<string> RowLabels
    {
        get => _rowLabels;
        set
        {
            _rowLabels = [.. (value ?? []).Select(label => label ?? string.Empty)];
            StateHasChanged();
        }
    }

    /// <summary>One label per column; only the first character of each is drawn (the cell is two wide).</summary>
    public IReadOnlyList<string> ColumnLabels
    {
        get => _columnLabels;
        set
        {
            _columnLabels = [.. (value ?? []).Select(label => label ?? string.Empty)];
            StateHasChanged();
        }
    }

    /// <summary>Which of the five levels (0 quiet … 4 hot) a value falls in; NaN is quiet.</summary>
    public static int Level(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        return Math.Clamp((int)Math.Floor(Math.Clamp(value, 0, 1) * Shades.Length), 0, Shades.Length - 1);
    }

    private int LabelWidth => _rowLabels.Length == 0 ? 0 : _rowLabels.Max(l => l.Length) + 1;

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        int height = (int)ViewSize.Height;
        int labelWidth = LabelWidth;
        int firstDataRow = _columnLabels.Length == 0 ? 0 : 1;
        int columns = Math.Max(0, (width - labelWidth) / 2);

        for (int y = 0; y < height; y++)
        {
            ConxelString line = new(string.Empty, colorScheme.Foreground, colorScheme.Background);
            if (y < firstDataRow)
            {
                line.Append(new string(' ', labelWidth), colorScheme.Foreground, colorScheme.Background);
                foreach (string label in _columnLabels.Take(columns))
                {
                    line.Append((label.Length == 0 ? " " : label[..1]) + " ", colorScheme.DisabledForeground, colorScheme.Background);
                }
            }
            else
            {
                int row = y - firstDataRow;
                if (row < _values.Length)
                {
                    string name = row < _rowLabels.Length ? _rowLabels[row] : string.Empty;
                    line.Append(name.PadRight(labelWidth)[..Math.Min(labelWidth, width)], colorScheme.DisabledForeground, colorScheme.Background);
                    foreach (double value in _values[row].Take(columns))
                    {
                        int level = Level(value);
                        line.Append(new string(Shades[level], 2), Ramp[level], colorScheme.Background);
                    }
                }
            }

            line.Expand(width, colorScheme.Foreground, colorScheme.Background);
            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + (uint)y), new Size(width, 1), line);
        }
    }
}

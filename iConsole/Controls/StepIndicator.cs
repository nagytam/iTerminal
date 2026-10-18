using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A one-line "where am I" marker for a multi-step flow: done steps as ●, the current one as ◉ in the
/// accent colour, the ones to come as ○, joined by ─ (<c>●─◉─○─○</c>). The width follows the step count.
/// </summary>
public class StepIndicator : Control
{
    private int _count;
    private int _current;

    public StepIndicator(IThemeService themeService) : base(themeService)
    {
        ViewSize = new Size(0, 1);
    }

    public int Count
    {
        get => _count;
        set
        {
            _count = Math.Max(0, value);
            ViewSize = new Size(WidthFor(_count), 1);
            StateHasChanged();
        }
    }

    public int Current
    {
        get => _current;
        set
        {
            _current = value;
            StateHasChanged();
        }
    }

    /// <summary>Cells needed for <paramref name="count"/> steps: one glyph each and a joint between neighbours.</summary>
    public static int WidthFor(int count) => count <= 0 ? 0 : count * 2 - 1;

    /// <summary>The glyph line for <paramref name="count"/> steps with <paramref name="current"/> (zero-based) the active one.</summary>
    public static string Render(int count, int current)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        char[] cells = new char[WidthFor(count)];
        for (int i = 0; i < count; i++)
        {
            cells[i * 2] = i < current ? '●' : i == current ? '◉' : '○';
            if (i < count - 1)
            {
                cells[i * 2 + 1] = '─';
            }
        }

        return new string(cells);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (_count <= 0)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        string line = Render(_count, _current);
        buffer.Write(ZOrder, AbsolutePosition, new Size((uint)line.Length, 1), new ConxelString(line, colorScheme.Foreground, colorScheme.Background));
        if (_current >= 0 && _current < _count)
        {
            buffer.Write(ZOrder, new Position(AbsolutePosition.X + _current * 2, AbsolutePosition.Y), new Size(1, 1), new ConxelString(line[_current * 2].ToString(), Color.Cyan, colorScheme.Background));
        }
    }
}

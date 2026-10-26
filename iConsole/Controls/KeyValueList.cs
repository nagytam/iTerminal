using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A read-only description list: one <c>Key   value</c> pair per row, the values lined up in a column
/// (<c>Name      iTerminal</c>). The key column is as wide as the longest key, capped at
/// <see cref="MaxKeyWidth"/>; values are cut to the remaining width. Every row of the control is painted,
/// the ones past the last pair blank, so a list that loses pairs does not keep showing them.
/// </summary>
public class KeyValueList : Control
{
    private readonly List<(string Key, string Value)> _items = [];

    public KeyValueList(IThemeService themeService) : base(themeService)
    {
        ViewSize = new Size(40, 0);
    }

    public IReadOnlyList<(string Key, string Value)> Items => _items;

    /// <summary>Widest the key column may grow to; longer keys are cut.</summary>
    public int MaxKeyWidth
    {
        get => _maxKeyWidth;
        set => _maxKeyWidth = Math.Max(0, value);
    }

    /// <summary>Cells between the key column and the values; a negative gap would fail every draw, so it is at least 0.</summary>
    public int Gap
    {
        get => _gap;
        set => _gap = Math.Max(0, value);
    }

    private int _maxKeyWidth = 20;
    private int _gap = 2;

    public void SetItems(IEnumerable<(string Key, string Value)> items)
    {
        _items.Clear();
        // A null key or value would fail in the width and cut calls on every draw; it is shown as an empty cell instead.
        _items.AddRange(items.Select(item => (item.Key ?? string.Empty, item.Value ?? string.Empty)));
        // Grows to fit but never shrinks: rows painted earlier sit outside a smaller rectangle and nothing
        // else repaints them, so a list that lost pairs has to keep the rows and paint them blank.
        ViewSize = new Size(ViewSize.Width, Math.Max(ViewSize.Height, _items.Count));
        StateHasChanged();
    }

    public void Add(string key, string value) => SetItems([.. _items, (key, value)]);

    /// <summary>Width of the key column for the current items.</summary>
    public int KeyWidth => Math.Min(MaxKeyWidth, _items.Count == 0 ? 0 : _items.Max(i => i.Key.Length));

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        int keyWidth = KeyWidth;
        int valueWidth = Math.Max(0, width - keyWidth - Gap);
        for (int y = 0; y < (int)ViewSize.Height; y++)
        {
            ConxelString line = new(string.Empty, colorScheme.Foreground, colorScheme.Background);
            if (y < _items.Count)
            {
                (string key, string value) = _items[y];
                line.Append(key.Cut(keyWidth).PadRight(keyWidth), colorScheme.DisabledForeground, colorScheme.Background);
                line.Append(new string(' ', Gap), colorScheme.Foreground, colorScheme.Background);
                line.Append(value.Cut(valueWidth), colorScheme.Foreground, colorScheme.Background);
            }

            line.Expand(width, colorScheme.Foreground, colorScheme.Background);
            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + (uint)y), new Size(width, 1), line);
        }
    }
}

using iConsole.Helper;
using iConsole.Model;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace iConsole.Display;

[DebuggerDisplay("{Text}")]
public class ConxelString : IEnumerable<Conxel>
{
    public List<Conxel> String { get; private set; } = [];

    public int Count => String.Count;

    public Conxel this[int index]
    {
        get
        {
            return String[index];
        }
    }

    public ConxelString()
    {
    }

    public ConxelString(char c, Color foreground, Color background,
        bool isBold = false, bool isItalic = false, bool isUnderline = false, bool isStrikeThrough = false)
    {
        Append(c, foreground, background, isBold, isItalic, isUnderline, isStrikeThrough);
    }

    public ConxelString(Span<Conxel> conxels)
    {
        String = new List<Conxel>(conxels.Length);
        foreach (var conxel in conxels)
        {
            String.Add(conxel);
        }
    }

    public ConxelString(string @string, Color foreground, Color background, 
        bool isBold = false, bool isItalic = false, bool isUnderline = false, bool isStrikeThrough = false)
    {
        String = new List<Conxel>(@string.Length);
        Append(@string, foreground, background, isBold, isItalic, isUnderline, isStrikeThrough);
    }

    public bool Any()
    {
        return String.Count != 0;
    }

    public void Append(Conxel conxel)
    {
        String.Add(conxel);
    }

    public void Append(char c, Color foregroundColor, Color backgroundColor,
        bool isBold = false, bool isItalic = false, bool isUnderline = false,
        bool isStrikeThrough = false)
    {
        String.Add(new Conxel(c, foregroundColor, backgroundColor,
            isBold, isItalic, isUnderline, isStrikeThrough));
    }

    public void Append(string? @string, Color foregroundColor, Color backgroundColor,
        bool isBold = false, bool isItalic = false, bool isUnderline = false,
        bool isStrikeThrough = false)
    {
        if (!string.IsNullOrEmpty(@string))
        {
            var source = @string.AsSpan();
            var length = source.Length;
            var startIndex = String.Count;
            CollectionsMarshal.SetCount(String, startIndex + length);
            var span = CollectionsMarshal.AsSpan(String).Slice(startIndex, length);
            var conxel = new Conxel('\0', foregroundColor, backgroundColor,
                isBold, isItalic, isUnderline, isStrikeThrough);
            for (var i = 0; i < length; i++)
            {
                conxel.Char = source[i];
                span[i] = conxel;
            }
        }
    }

    public void Append(ConxelString conxelString)
    {
        foreach (var conxel in conxelString)
        {
            String.Add(conxel);
        }
    }

    public void Expand(int width, Color foregroundColor, Color backgroundColor)
    {
        while (Count < width)
        {
            String.Add(new Conxel(' ', foregroundColor, backgroundColor));
        }
    }

    /// <summary>
    /// Replaces each cell from <paramref name="start"/> up to, not including, <paramref name="end"/> with what
    /// <paramref name="edit"/> returns for it.
    /// </summary>
    public void Edit(int start, int end, Func<Conxel, Conxel> edit)
    {
        Span<Conxel> cells = CollectionsMarshal.AsSpan(String);
        for (int x = Math.Max(0, start); x < Math.Min(end, cells.Length); x++)
        {
            cells[x] = edit(cells[x]);
        }
    }

    /// <summary>
    /// Makes the cells from <paramref name="start"/> up to <paramref name="end"/> a hyperlink to <paramref name="target"/>,
    /// or removes the link when it is null or empty. The terminal opens it on a Ctrl+click or the like; the text itself
    /// is not restyled, so underline it as well if it should look like a link.
    /// </summary>
    public void SetHyperlink(string? target, int start = 0, int end = int.MaxValue)
    {
        ushort id = Hyperlinks.Register(target);
        Edit(start, end, conxel =>
        {
            conxel.LinkId = id;
            return conxel;
        });
    }

    /// <summary>Underlines the cells in the given style, and in <paramref name="color"/> when one is given.</summary>
    public void SetUnderline(UnderlineStyle style, Color? color = null, int start = 0, int end = int.MaxValue)
    {
        Edit(start, end, conxel =>
        {
            conxel.IsUnderline = true;
            conxel.UnderlineStyle = style;
            conxel.HasUnderlineColor = color is not null;
            conxel.UnderlineColor = color ?? default;
            return conxel;
        });
    }

    public void HighlightHotKey(ConsoleKey? hotConsoleKey, char? hotKeyChar, Color foreground, Color background)
    {
        char? c = null;
        if (hotConsoleKey is not null)
        {
            c = hotConsoleKey?.ConsoleKeyToChar();
        }
        else if (hotKeyChar is not null)
        {
            c = hotKeyChar;
        }
        if (c is not null)
        {
            var index = Text.IndexOf(c.Value, StringComparison.InvariantCultureIgnoreCase);
            if (index >= 0)
            {
                var cell = String[index];
                cell.ForegroundColor = foreground;
                cell.BackgroundColor = background;
                String[index] = cell;
            }
        }
    }

    public IEnumerator<Conxel> GetEnumerator()
    {
        return String.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return String.GetEnumerator();
    }

    public void HighlightRange(int start, int end, Color foreground, Color background)
    {
        for (int x = start; x < Math.Min(end, Count); x++)
        {
            var cell = String[x];
            cell.ForegroundColor = foreground;
            cell.BackgroundColor = background;
            String[x] = cell;
        }
    }

    public void HighlightSelection(ISelectionTextModel selectionTextModel, IScrollableModel scrollableModel, int startPos, Color foreground, Color background)
    {
        if (selectionTextModel.IsSelected && selectionTextModel.Start is not null && selectionTextModel.End is not null)
        {
            var selectionStart = Math.Max(0, startPos + selectionTextModel.Start.X - scrollableModel.ScrollPosition.X);
            var selectionEnd = Math.Min(startPos + selectionTextModel.End.X - scrollableModel.ScrollPosition.X + 1, Count);
            HighlightRange((int)selectionStart, (int)selectionEnd, foreground, background);
        } 
    }

    public string Text
    {
        get
        {
            StringBuilder builder = new();
            foreach (var conxel in String)
            {
                builder.Append(conxel.Char);
            }
            return builder.ToString();
        }
    }
}

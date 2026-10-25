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
        if (!string.IsNullOrEmpty(@string) && TextWidth.NeedsCellLayout(@string))
        {
            // Wide characters, emoji and combining marks take a different number of cells from characters.
            var conxel = new Conxel('\0', foregroundColor, backgroundColor,
                isBold, isItalic, isUnderline, isStrikeThrough);
            foreach (var cell in TextWidth.ToCells(@string))
            {
                conxel.Char = cell;
                String.Add(conxel);
            }
        }
        else if (!string.IsNullOrEmpty(@string))
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
            // The text as a person reads it: continuation cells left out, surrogate pairs joined.
            return TextWidth.FromCells(String.Select(conxel => conxel.Char));
        }
    }
}

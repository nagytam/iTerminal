using System.Globalization;
using System.Text;

namespace iConsole.Display;

/// <summary>
/// How many terminal columns text takes, and how it is laid out in cells.
///
/// <para>A cell holds one UTF-16 char, so a character that takes two columns needs a second cell:</para>
/// <list type="bullet">
/// <item>A wide BMP character (for example a CJK ideograph) is stored as the character followed by
/// <see cref="Continuation"/>. The continuation cell is never written to the terminal: the wide
/// character moves the cursor over both columns.</item>
/// <item>A wide character outside the BMP (most emoji) is stored as its surrogate pair. The pair already
/// takes two cells, so it needs no continuation.</item>
/// <item>A zero-width character (combining marks, the zero-width joiner, variation selectors) has no cell of
/// its own and is dropped. Accents that arrive decomposed are lost; precomposed text is unaffected.</item>
/// <item>A narrow character outside the BMP cannot fit a single cell, so it is shown as U+FFFD.</item>
/// </list>
///
/// <para>The width table covers the East Asian Wide and Fullwidth ranges and the common emoji-presentation
/// ranges. It is not the full Unicode table; a character not listed is one column.</para>
/// </summary>
public static class TextWidth
{
    /// <summary>Stored in the cell after a wide character. Never written to the terminal.</summary>
    public const char Continuation = '￿';

    /// <summary>Shown in place of a narrow character outside the BMP, which has no single cell to occupy.</summary>
    public const char Replacement = '�';

    private static readonly (int First, int Last)[] WideRanges =
    [
        (0x1100, 0x115F), (0x231A, 0x231B), (0x2329, 0x232A), (0x23E9, 0x23EC), (0x23F0, 0x23F0),
        (0x23F3, 0x23F3), (0x25FD, 0x25FE), (0x2614, 0x2615), (0x2648, 0x2653), (0x267F, 0x267F),
        (0x2693, 0x2693), (0x26A1, 0x26A1), (0x26AA, 0x26AB), (0x26BD, 0x26BE), (0x26C4, 0x26C5),
        (0x26CE, 0x26CE), (0x26D4, 0x26D4), (0x26EA, 0x26EA), (0x26F2, 0x26F3), (0x26F5, 0x26F5),
        (0x26FA, 0x26FA), (0x26FD, 0x26FD), (0x2705, 0x2705), (0x270A, 0x270B), (0x2728, 0x2728),
        (0x274C, 0x274C), (0x274E, 0x274E), (0x2753, 0x2755), (0x2757, 0x2757), (0x2795, 0x2797),
        (0x27B0, 0x27B0), (0x27BF, 0x27BF), (0x2B1B, 0x2B1C), (0x2B50, 0x2B50), (0x2B55, 0x2B55),
        (0x2E80, 0x303E), (0x3041, 0x33FF), (0x3400, 0x4DBF), (0x4E00, 0x9FFF), (0xA000, 0xA4CF),
        (0xAC00, 0xD7A3), (0xF900, 0xFAFF), (0xFE10, 0xFE19), (0xFE30, 0xFE6F), (0xFF00, 0xFF60),
        (0xFFE0, 0xFFE6), (0x1F004, 0x1F004), (0x1F0CF, 0x1F0CF), (0x1F18E, 0x1F18E),
        (0x1F191, 0x1F19A), (0x1F1E6, 0x1F1FF), (0x1F200, 0x1F251), (0x1F300, 0x1F64F),
        (0x1F680, 0x1F6FF), (0x1F7E0, 0x1F7EB), (0x1F90C, 0x1F9FF), (0x1FA70, 0x1FAFF),
        (0x20000, 0x2FFFD), (0x30000, 0x3FFFD),
    ];

    /// <summary>Columns taken by one code point: 0, 1 or 2.</summary>
    public static int Width(int codePoint)
    {
        if (codePoint < 0x0300)
        {
            return codePoint < 0x20 || codePoint == 0x7F ? 0 : 1;
        }

        if (IsZeroWidth(codePoint))
        {
            return 0;
        }

        return IsWide(codePoint) ? 2 : 1;
    }

    /// <summary>Columns taken by a rune.</summary>
    public static int Width(Rune rune) => Width(rune.Value);

    /// <summary>Columns taken by <paramref name="text"/> when drawn.</summary>
    public static int Of(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var columns = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            columns += Width(rune);
        }
        return columns;
    }

    /// <summary>
    /// <paramref name="text"/> cut or padded to exactly <paramref name="columns"/> columns, counting wide characters as two.
    /// A wide character that would be split by the cut is left out, and its column is padded with a space.
    /// </summary>
    public static string Fit(string? text, int columns)
    {
        columns = Math.Max(0, columns);
        var builder = new StringBuilder();
        var used = 0;
        foreach (var rune in (text ?? string.Empty).EnumerateRunes())
        {
            var width = Width(rune);
            if (width == 0)
            {
                continue;
            }
            if (used + width > columns)
            {
                break;
            }
            builder.Append(rune.ToString());
            used += width;
        }
        builder.Append(' ', columns - used);
        return builder.ToString();
    }

    /// <summary>
    /// True when the text needs the cell layout described on this class: a character from U+0300 upwards,
    /// which covers combining marks, wide characters and every character outside the BMP. Plain ASCII and
    /// Latin text skip the conversion entirely.
    /// </summary>
    public static bool NeedsCellLayout(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (c >= 0x0300 || c < 0x20 || c == 0x7F)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The text as the cells it occupies: wide BMP characters are followed by <see cref="Continuation"/>, wide
    /// characters outside the BMP are kept as surrogate pairs, zero-width characters are dropped and narrow
    /// characters outside the BMP become <see cref="Replacement"/>.
    /// </summary>
    public static IEnumerable<char> ToCells(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            switch (Width(rune))
            {
                case 0:
                    break;
                case 1 when rune.IsBmp:
                    yield return (char)rune.Value;
                    break;
                case 1:
                    yield return Replacement;
                    break;
                case 2 when rune.IsBmp:
                    yield return (char)rune.Value;
                    yield return Continuation;
                    break;
                default:
                    foreach (var unit in rune.ToString())
                    {
                        yield return unit;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// The text a row of cells reads as, with continuation cells left out and surrogate pairs joined: what
    /// a person sees on the screen.
    /// </summary>
    public static string FromCells(IEnumerable<char> cells)
    {
        var builder = new StringBuilder();
        foreach (var cell in cells)
        {
            if (cell != Continuation)
            {
                builder.Append(cell);
            }
        }
        return builder.ToString();
    }

    private static bool IsZeroWidth(int codePoint)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(codePoint);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.Format
            or UnicodeCategory.Control;
    }

    private static bool IsWide(int codePoint)
    {
        var low = 0;
        var high = WideRanges.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var (first, last) = WideRanges[middle];
            if (codePoint < first)
            {
                high = middle - 1;
            }
            else if (codePoint > last)
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }
        return false;
    }
}

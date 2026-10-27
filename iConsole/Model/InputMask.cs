using System.Text;

namespace iConsole.Model;

/// <summary>
/// An input pattern for a <c>TextBox</c>, such as <c>(000) 000-0000</c> or <c>&gt;LL-00</c>. A pattern is made of <i>slots</i>, which
/// the user fills, and literals, which the box types itself:
/// <list type="bullet">
/// <item><c>0</c> a digit, <c>9</c> a digit that may be left out</item>
/// <item><c>L</c> a letter, <c>?</c> a letter that may be left out</item>
/// <item><c>A</c> a letter or digit, <c>a</c> one that may be left out</item>
/// <item><c>&amp;</c> any printable character</item>
/// <item><c>&gt;</c> upper-cases the slots after it, <c>&lt;</c> lower-cases them, <c>|</c> stops either</item>
/// <item><c>\</c> makes the next character a literal; any other character is a literal</item>
/// </list>
/// The value the user typed, without the literals, is the <i>raw</i> text; <see cref="Format"/> puts the literals back.
/// A literal is only shown once a character follows it, so nothing is typed ahead of the user.
/// </summary>
public sealed class InputMask
{
    private enum SlotKind
    {
        Literal,
        Digit,
        Letter,
        Alphanumeric,
        Any,
    }

    private enum CaseMode
    {
        None,
        Upper,
        Lower,
    }

    private readonly record struct Element(SlotKind Kind, char Literal, bool Optional, CaseMode Case);

    private readonly List<Element> _elements = [];
    private readonly List<int> _slotElement = [];

    public InputMask(string pattern)
    {
        Pattern = pattern ?? string.Empty;
        CaseMode mode = CaseMode.None;
        for (int i = 0; i < Pattern.Length; i++)
        {
            char c = Pattern[i];
            switch (c)
            {
                case '>': mode = CaseMode.Upper; continue;
                case '<': mode = CaseMode.Lower; continue;
                case '|': mode = CaseMode.None; continue;
                case '\\' when i + 1 < Pattern.Length:
                    _elements.Add(new Element(SlotKind.Literal, Pattern[++i], false, CaseMode.None));
                    continue;
            }

            Element element = c switch
            {
                '0' => new Element(SlotKind.Digit, c, false, mode),
                '9' => new Element(SlotKind.Digit, c, true, mode),
                'L' => new Element(SlotKind.Letter, c, false, mode),
                '?' => new Element(SlotKind.Letter, c, true, mode),
                'A' => new Element(SlotKind.Alphanumeric, c, false, mode),
                'a' => new Element(SlotKind.Alphanumeric, c, true, mode),
                '&' => new Element(SlotKind.Any, c, false, mode),
                _ => new Element(SlotKind.Literal, c, false, CaseMode.None),
            };
            if (element.Kind != SlotKind.Literal)
            {
                _slotElement.Add(_elements.Count);
            }

            _elements.Add(element);
        }
    }

    public string Pattern { get; }

    /// <summary>The number of characters the user can type.</summary>
    public int SlotCount => _slotElement.Count;

    /// <summary>The number of slots that must be filled for the value to be complete.</summary>
    public int RequiredCount => _slotElement.Count(index => !_elements[index].Optional);

    /// <summary>The whole pattern as it looks empty: slots as <c>_</c>, literals as themselves.</summary>
    public string Template => new(_elements.Select(e => e.Kind == SlotKind.Literal ? e.Literal : '_').ToArray());

    /// <summary>Whether slot <paramref name="slot"/> takes <paramref name="c"/>.</summary>
    public bool Accepts(int slot, char c)
    {
        if (slot < 0 || slot >= _slotElement.Count)
        {
            return false;
        }

        return _elements[_slotElement[slot]].Kind switch
        {
            SlotKind.Digit => c is >= '0' and <= '9',
            SlotKind.Letter => char.IsLetter(c),
            SlotKind.Alphanumeric => char.IsLetterOrDigit(c),
            SlotKind.Any => !char.IsControl(c),
            _ => false,
        };
    }

    /// <summary><paramref name="c"/> in the case slot <paramref name="slot"/> asks for.</summary>
    public char Normalize(int slot, char c)
    {
        if (slot < 0 || slot >= _slotElement.Count)
        {
            return c;
        }

        return _elements[_slotElement[slot]].Case switch
        {
            CaseMode.Upper => char.ToUpperInvariant(c),
            CaseMode.Lower => char.ToLowerInvariant(c),
            _ => c,
        };
    }

    /// <summary>True when every slot of <paramref name="raw"/> takes its character and no more were typed than there are slots.</summary>
    public bool IsValidRaw(string raw)
    {
        if (raw.Length > SlotCount)
        {
            return false;
        }

        for (int i = 0; i < raw.Length; i++)
        {
            if (!Accepts(i, raw[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when the raw text fills every required slot.</summary>
    public bool IsComplete(string raw)
    {
        if (!IsValidRaw(raw))
        {
            return false;
        }

        for (int slot = 0; slot < SlotCount; slot++)
        {
            bool optional = _elements[_slotElement[slot]].Optional;
            if (!optional && slot >= raw.Length)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The text with its literals: characters of <paramref name="raw"/> in their slots, and the literals that come before each one.</summary>
    public string Format(string raw)
    {
        var text = new StringBuilder();
        int filled = 0;
        foreach (Element element in _elements)
        {
            if (element.Kind == SlotKind.Literal)
            {
                if (filled < raw.Length)
                {
                    text.Append(element.Literal);
                }

                continue;
            }

            if (filled >= raw.Length)
            {
                break;
            }

            text.Append(Normalize(filled, raw[filled]));
            filled++;
        }

        return text.ToString();
    }

    /// <summary>
    /// The raw text in <paramref name="formatted"/>: literals are skipped where they stand, and characters a slot does not take
    /// are dropped. Text that was typed without the literals ("5551234567") gives the same raw text as the formatted one.
    /// </summary>
    public string ExtractRaw(string? formatted)
    {
        formatted ??= string.Empty;
        var raw = new StringBuilder();
        int i = 0;
        foreach (Element element in _elements)
        {
            if (i >= formatted.Length)
            {
                break;
            }

            if (element.Kind == SlotKind.Literal)
            {
                if (formatted[i] == element.Literal)
                {
                    i++;
                }

                continue;
            }

            while (i < formatted.Length && !Accepts(raw.Length, formatted[i]))
            {
                i++;
            }

            if (i < formatted.Length)
            {
                raw.Append(Normalize(raw.Length, formatted[i]));
                i++;
            }
        }

        return raw.ToString();
    }

    /// <summary>How many slots lie before character position <paramref name="position"/> of the formatted text.</summary>
    public int SlotsBefore(string formatted, int position)
    {
        position = Math.Clamp(position, 0, formatted.Length);
        // Format is deterministic, so the slot count of a prefix is the length of the raw text that formats to it.
        int slot = 0;
        string raw = ExtractRaw(formatted);
        while (slot < raw.Length && Format(raw[..(slot + 1)]).Length <= position)
        {
            slot++;
        }

        return slot;
    }

    /// <summary>The length of the formatted text that ends with slot number <paramref name="slots"/> (0 for none).</summary>
    public int LengthThrough(string raw, int slots)
    {
        return Format(raw[..Math.Clamp(slots, 0, raw.Length)]).Length;
    }

    /// <summary>The part of <see cref="Template"/> after the first <paramref name="typedLength"/> characters: what is still to type.</summary>
    public string TemplateAfter(int typedLength)
    {
        string template = Template;
        return typedLength >= template.Length ? string.Empty : template[Math.Max(0, typedLength)..];
    }
}

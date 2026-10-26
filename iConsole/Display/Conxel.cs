using System.Runtime.InteropServices;

namespace iConsole.Display;

[StructLayout(LayoutKind.Sequential)]
public record struct Conxel
{
    public char Char { get; set; }
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public bool IsUnderline { get; set; }
    public bool IsStrikeThrough { get; set; }

    /// <summary>Faint text (SGR 2).</summary>
    public bool IsDim { get; set; }

    /// <summary>Foreground and background swapped (SGR 7).</summary>
    public bool IsReverse { get; set; }

    /// <summary>Blinking text (SGR 5). Many terminals ignore it, or let the user turn it off.</summary>
    public bool IsBlink { get; set; }

    /// <summary>A line over the text (SGR 53).</summary>
    public bool IsOverline { get; set; }

    /// <summary>The line style when <see cref="IsUnderline"/> is set.</summary>
    public UnderlineStyle UnderlineStyle { get; set; }

    /// <summary>The underline's own colour, used when <see cref="HasUnderlineColor"/> is set; otherwise it follows the text.</summary>
    public Color UnderlineColor { get; set; }

    public bool HasUnderlineColor { get; set; }

    /// <summary>
    /// The link this cell belongs to, as handed out by <see cref="Hyperlinks.Register"/>; <see cref="Hyperlinks.None"/> for
    /// none. Written as an OSC 8 hyperlink, so a terminal that supports them makes the text clickable.
    /// </summary>
    public ushort LinkId { get; set; }
    public Color ForegroundColor { get; set; }
    public Color BackgroundColor { get; set; }

    public Conxel(char c, Color foregroundColor, Color backgroundColor, 
        bool isBold = false, bool isItalic = false, bool isUnderline = false,
        bool isStrikeThrough = false)
    {
        Char = c;
        IsBold = isBold;
        IsItalic = isItalic;
        IsUnderline = isUnderline;
        IsStrikeThrough = isStrikeThrough;
        ForegroundColor = foregroundColor;
        BackgroundColor = backgroundColor;
    }
}

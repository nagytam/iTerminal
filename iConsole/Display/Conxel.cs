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

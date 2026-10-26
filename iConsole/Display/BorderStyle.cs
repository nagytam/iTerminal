namespace iConsole.Display;

/// <summary>The line set a box border is drawn with.</summary>
public enum BorderStyle
{
    /// <summary>No border: the box takes no cell for it.</summary>
    None,

    /// <summary>A thin line.</summary>
    Single,

    /// <summary>A double line, the style of <see cref="Border"/>.</summary>
    Double,

    /// <summary>A thin line with rounded corners.</summary>
    Rounded,

    /// <summary>A heavy line.</summary>
    Thick,

    /// <summary>A thin dashed line.</summary>
    Dashed,
}

/// <summary>Where text sits along an edge.</summary>
public enum TextAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>The six glyphs of one border style.</summary>
public sealed record BorderGlyphs(
    char TopLeft, char TopRight, char BottomLeft, char BottomRight, char Horizontal, char Vertical)
{
    /// <summary>The glyphs of <paramref name="style"/>; <see cref="BorderStyle.None"/> gives spaces.</summary>
    public static BorderGlyphs For(BorderStyle style)
    {
        return style switch
        {
            BorderStyle.Single => new BorderGlyphs('┌', '┐', '└', '┘', '─', '│'),
            BorderStyle.Double => new BorderGlyphs('╔', '╗', '╚', '╝', '═', '║'),
            BorderStyle.Rounded => new BorderGlyphs('╭', '╮', '╰', '╯', '─', '│'),
            BorderStyle.Thick => new BorderGlyphs('┏', '┓', '┗', '┛', '━', '┃'),
            BorderStyle.Dashed => new BorderGlyphs('┌', '┐', '└', '┘', '╌', '╎'),
            _ => new BorderGlyphs(' ', ' ', ' ', ' ', ' ', ' '),
        };
    }
}

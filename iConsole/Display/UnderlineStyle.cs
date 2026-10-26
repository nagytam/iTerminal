namespace iConsole.Display;

/// <summary>
/// How an underlined cell's line is drawn. Only read when <see cref="Conxel.IsUnderline"/> is set; a terminal that
/// does not know the style draws a plain single underline instead.
/// </summary>
public enum UnderlineStyle : byte
{
    Single = 0,
    Double,
    /// <summary>The wavy "squiggle" an editor puts under a misspelt word or a compile error.</summary>
    Curly,
    Dotted,
    Dashed,
}

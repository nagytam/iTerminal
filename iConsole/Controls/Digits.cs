using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>How large the characters of a <see cref="Digits"/> control are drawn.</summary>
public enum DigitsStyle
{
    /// <summary>Each dot of the 3×5 font is two cells wide and one tall: a digit is 6 columns by 5 rows.</summary>
    Large,

    /// <summary>Each dot is one cell wide and half a row tall, drawn with half blocks: a digit is 3 columns by 3 rows.</summary>
    Compact,
}

public enum DigitsAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Large numerals for a clock, a counter or a headline figure: the digits 0–9, the hexadecimal letters A–F, and
/// <c>: . , - + / % space</c>, drawn with block characters in a 3×5 dot font. Anything else is left blank. It paints its
/// whole rectangle on every draw, filling what the text does not cover with the background.
/// </summary>
public class Digits : Control
{
    // One entry per character: five rows of dots, '#' for a lit dot. Most glyphs are three dots wide; ':' '.' ',' are one.
    private static readonly Dictionary<char, string[]> s_font = new()
    {
        ['0'] = ["###", "# #", "# #", "# #", "###"],
        ['1'] = [" # ", "## ", " # ", " # ", "###"],
        ['2'] = ["###", "  #", "###", "#  ", "###"],
        ['3'] = ["###", "  #", "###", "  #", "###"],
        ['4'] = ["# #", "# #", "###", "  #", "  #"],
        ['5'] = ["###", "#  ", "###", "  #", "###"],
        ['6'] = ["###", "#  ", "###", "# #", "###"],
        ['7'] = ["###", "  #", "  #", "  #", "  #"],
        ['8'] = ["###", "# #", "###", "# #", "###"],
        ['9'] = ["###", "# #", "###", "  #", "###"],
        ['A'] = ["###", "# #", "###", "# #", "# #"],
        ['B'] = ["## ", "# #", "## ", "# #", "## "],
        ['C'] = ["###", "#  ", "#  ", "#  ", "###"],
        ['D'] = ["## ", "# #", "# #", "# #", "## "],
        ['E'] = ["###", "#  ", "###", "#  ", "###"],
        ['F'] = ["###", "#  ", "###", "#  ", "#  "],
        [':'] = [" ", "#", " ", "#", " "],
        ['.'] = [" ", " ", " ", " ", "#"],
        [','] = [" ", " ", " ", "#", "#"],
        ['-'] = ["   ", "   ", "###", "   ", "   "],
        ['+'] = ["   ", " # ", "###", " # ", "   "],
        ['/'] = ["  #", "  #", " # ", "#  ", "#  "],
        ['%'] = ["# #", "  #", " # ", "#  ", "# #"],
        [' '] = ["   ", "   ", "   ", "   ", "   "],
    };

    private string _text = string.Empty;
    private DigitsStyle _style = DigitsStyle.Large;

    public Digits(IThemeService themeService) : base(themeService)
    {
    }

    /// <summary>What to draw. Letters are drawn as capitals.</summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            StateHasChanged();
        }
    }

    public DigitsStyle Style
    {
        get => _style;
        set
        {
            _style = value;
            StateHasChanged();
        }
    }

    public DigitsAlignment Alignment { get; set; } = DigitsAlignment.Left;

    /// <summary>The colour of the dots; the theme's foreground when null.</summary>
    public Color? ForegroundColor { get; set; }

    /// <summary>The colour behind the dots; the theme's background when null.</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>The size the current text needs, for a layout that sizes the control to fit.</summary>
    public Size MeasuredSize => new(Measure(_text, _style), HeightOf(_style));

    /// <summary>Rows a character is tall in <paramref name="style"/>.</summary>
    public static int HeightOf(DigitsStyle style) => style == DigitsStyle.Large ? 5 : 3;

    /// <summary>Columns <paramref name="text"/> takes in <paramref name="style"/>: its characters and one blank column between them.</summary>
    public static int Measure(string text, DigitsStyle style)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int cells = style == DigitsStyle.Large ? 2 : 1;
        int total = 0;
        foreach (char c in text)
        {
            total += GlyphOf(c)[0].Length * cells;
        }

        return total + (text.Length - 1) * cells;
    }

    /// <summary>
    /// The rows of text for <paramref name="text"/>, all of the same width. Large dots are <c>██</c>; compact dots are the
    /// half blocks <c>▀</c>, <c>▄</c> and <c>█</c>, two dot rows to a text row.
    /// </summary>
    public static IReadOnlyList<string> Render(string text, DigitsStyle style)
    {
        int height = HeightOf(style);
        int width = Measure(text, style);
        char[][] rows = [.. Enumerable.Range(0, height).Select(_ => new char[width])];
        foreach (char[] row in rows)
        {
            Array.Fill(row, ' ');
        }

        int x = 0;
        int cells = style == DigitsStyle.Large ? 2 : 1;
        for (int index = 0; index < (text?.Length ?? 0); index++)
        {
            string[] glyph = GlyphOf(text![index]);
            int glyphWidth = glyph[0].Length;
            for (int dotColumn = 0; dotColumn < glyphWidth; dotColumn++)
            {
                for (int dotRow = 0; dotRow < 5; dotRow++)
                {
                    if (glyph[dotRow][dotColumn] != '#')
                    {
                        continue;
                    }

                    for (int cell = 0; cell < cells; cell++)
                    {
                        int column = x + dotColumn * cells + cell;
                        if (style == DigitsStyle.Large)
                        {
                            rows[dotRow][column] = '█';
                        }
                        else
                        {
                            // Two dot rows share one text row: the upper one is the top half, the lower one the bottom half.
                            int row = dotRow / 2;
                            bool top = dotRow % 2 == 0;
                            char current = rows[row][column];
                            bool hasTop = top || current is '▀' or '█';
                            bool hasBottom = !top || current is '▄' or '█';
                            rows[row][column] = hasTop && hasBottom ? '█' : hasTop ? '▀' : '▄';
                        }
                    }
                }
            }

            x += (glyphWidth + 1) * cells;
        }

        return [.. rows.Select(row => new string(row))];
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        Color foreground = ForegroundColor ?? colorScheme.Foreground;
        Color background = BackgroundColor ?? colorScheme.Background;
        int width = (int)ViewSize.Width;
        IReadOnlyList<string> rows = Render(_text, _style);
        int offset = Alignment switch
        {
            DigitsAlignment.Right => Math.Max(0, width - Measure(_text, _style)),
            DigitsAlignment.Center => Math.Max(0, (width - Measure(_text, _style)) / 2),
            _ => 0,
        };

        // Every row of the control is painted, the ones below the digits too: nothing clears a layer between frames.
        for (int y = 0; y < ViewSize.Height; y++)
        {
            string line = y < rows.Count ? new string(' ', offset) + rows[y] : string.Empty;
            line = line.Length > width ? line[..width] : line.PadRight(width);
            buffer.Write(
                ZOrder,
                new Position(AbsolutePosition.X, AbsolutePosition.Y + y),
                new Size(width, 1),
                new ConxelString(line, foreground, background));
        }
    }

    private static string[] GlyphOf(char c)
    {
        return s_font.TryGetValue(char.ToUpperInvariant(c), out string[]? glyph) ? glyph : s_font[' '];
    }
}

using iConsole.Display;
using System.Text;

namespace iConsole.Drivers;

public static class AnsiRenderer
{
    public const char    BELL = '\x7';
    public const string  CLEAR_DISPLAY = CSI + "2J";
    public const string  SHOW_CURSOR = CSI + "?25h";
    public const string  HIDE_CURSOR = CSI + "?25l";
    private const char   ESCAPE = '\x1B';
    private const string CSI = "\x1B["; // Control Sequence Introducer
    private const char   SEMICOLON = ';';
    private const char   BRACKET_OPEN = '[';
    private const char   END = 'm';
    private const char   BOLD = '1';
    private const char   ITALIC = '3';
    private const char   UNDERLINE = '4';
    private const char   BLINK = '5';
    private const string DOUBLE_UNDERLINE = "21";
    private const char   STRIKE_THROUGH = '9';
    private const char   H = 'H';
    private const string RESET = "[0m";

    private const string FOREGROUND_BLACK = "30";
    private const string FOREGROUND_RED = "31";
    private const string FOREGROUND_GREEN = "32";
    private const string FOREGROUND_YELLOW = "33";
    private const string FOREGROUND_BLUE = "34";
    private const string FOREGROUND_MAGENTA = "35";
    private const string FOREGROUND_CYAN = "36";
    private const string FOREGROUND_WHITE = "37";

    private const string BACKGROUND_BLACK = "40";
    private const string BACKGROUND_RED = "41";
    private const string BACKGROUND_GREEN = "42";
    private const string BACKGROUND_YELLOW = "43";
    private const string BACKGROUND_BLUE = "44";
    private const string BACKGROUND_MAGENTA = "45";
    private const string BACKGROUND_CYAN = "46";
    private const string BACKGROUND_WHITE = "47";

    private const string FOREGROUND_BLACK_BRIGHT = "90";
    private const string FOREGROUND_RED_BRIGHT = "91";
    private const string FOREGROUND_GREEN_BRIGHT = "92";
    private const string FOREGROUND_YELLOW_BRIGHT = "93";
    private const string FOREGROUND_BLUE_BIGHT = "94";
    private const string FOREGROUND_MAGENTA_BRIGHT = "95";
    private const string FOREGROUND_CYAN_BRIGHT = "96";
    private const string FOREGROUND_WHITE_BRIGHT = "97";

    private const string BACKGROUND_BLACK_BRIGHT = "100";
    private const string BACKGROUND_RED_BRIGHT = "101";
    private const string BACKGROUND_GREEN_BRIGHT = "102";
    private const string BACKGROUND_YELLOW_BRIGHT = "103";
    private const string BACKGROUND_BLUE_BRIGHT = "104";
    private const string BACKGROUND_MAGENTA_BRIGHT = "105";
    private const string BACKGROUND_CYAN_BRIGHT = "106";
    private const string BACKGROUND_WHITE_BRIGHT = "107";

    // O(1) lookup tables indexed by Color enum ordinal (mirrors ConsoleColor 0-15).
    // Avoids the large switch chains that the JIT cannot easily optimise into a jump table.
    private static readonly string[] s_fgCodes =
    [
        "30",  // 0  Black
        "34",  // 1  DarkBlue
        "32",  // 2  DarkGreen
        "36",  // 3  DarkCyan
        "31",  // 4  DarkRed
        "35",  // 5  DarkMagenta
        "33",  // 6  DarkYellow
        "37",  // 7  Gray
        "90",  // 8  DarkGray
        "94",  // 9  Blue
        "92",  // 10 Green
        "96",  // 11 Cyan
        "91",  // 12 Red
        "95",  // 13 Magenta
        "93",  // 14 Yellow
        "97",  // 15 White
    ];

    private static readonly string[] s_bgCodes =
    [
        "40",  // 0  Black
        "44",  // 1  DarkBlue
        "42",  // 2  DarkGreen
        "46",  // 3  DarkCyan
        "41",  // 4  DarkRed
        "45",  // 5  DarkMagenta
        "43",  // 6  DarkYellow
        "47",  // 7  Gray
        "100", // 8  DarkGray
        "104", // 9  Blue
        "102", // 10 Green
        "106", // 11 Cyan
        "101", // 12 Red
        "105", // 13 Magenta
        "103", // 14 Yellow
        "107", // 15 White
    ];


    public static StringBuilder Conxel2Ansi(int x, int y, Conxel conxel)
    {
        var stringBuilder = new StringBuilder(40);
        // Cursor position
        stringBuilder.Append(CSI);
        stringBuilder.Append(y + 1);
        stringBuilder.Append(SEMICOLON);
        stringBuilder.Append(x + 1);
        stringBuilder.Append(H);
        // Color, style, character, reset
        AppendColorAndStyle(stringBuilder, conxel);
        stringBuilder.Append(conxel.Char);
        stringBuilder.Append(ESCAPE);
        stringBuilder.Append(RESET);
        return stringBuilder;
    }

    /// <summary>Emits <c>CSI fg;bg[;styles]m</c> into <paramref name="sb"/>.</summary>
    private static void AppendColorAndStyle(StringBuilder sb, in Conxel conxel)
    {
        sb.Append(CSI);
        sb.Append(s_fgCodes[(int)conxel.ForegroundColor]);
        sb.Append(SEMICOLON);
        sb.Append(s_bgCodes[(int)conxel.BackgroundColor]);
        if (conxel.IsBold)          { sb.Append(SEMICOLON); sb.Append(BOLD); }
        if (conxel.IsItalic)        { sb.Append(SEMICOLON); sb.Append(ITALIC); }
        if (conxel.IsStrikeThrough) { sb.Append(SEMICOLON); sb.Append(STRIKE_THROUGH); }
        if (conxel.IsUnderline)     { sb.Append(SEMICOLON); sb.Append(UNDERLINE); }
        sb.Append(END);
    }

    /// <summary>
    /// Renders a <see cref="ConxelString"/> to ANSI with a single cursor-position command
    /// followed by per-character text output; a new color/style escape is emitted only when
    /// the style changes between adjacent cells, minimising total output size.
    /// </summary>
    public static string ConxelString2Ansi(int x, int y, ConxelString conxelString)
    {
        var list = conxelString.String;
        if (list.Count == 0) return string.Empty;

        // Capacity estimate: cursor-pos (~12) + per-cell average of 2 chars for runs
        // with uniform colour, up to ~25 for every cell having a different colour.
        var sb = new StringBuilder(list.Count * 2 + 40);

        // Position cursor once for the whole string.
        sb.Append(CSI);
        sb.Append(y + 1);
        sb.Append(SEMICOLON);
        sb.Append(x + 1);
        sb.Append(H);

        var lastFg = (Color)(-1);
        var lastBg = (Color)(-1);
        var lastBold = false;
        var lastItalic = false;
        var lastUnderline = false;
        var lastStrike = false;
        bool stylesActive = false;

        foreach (var c in list)
        {
            // The second column of a wide character is covered by the character before it.
            if (c.Char == TextWidth.Continuation)
            {
                continue;
            }

            bool colorChanged = c.ForegroundColor != lastFg || c.BackgroundColor != lastBg;
            bool styleChanged = c.IsBold != lastBold || c.IsItalic != lastItalic
                             || c.IsUnderline != lastUnderline || c.IsStrikeThrough != lastStrike;

            if (colorChanged || styleChanged)
            {
                AppendColorAndStyle(sb, c);
                lastFg = c.ForegroundColor;
                lastBg = c.BackgroundColor;
                lastBold = c.IsBold;
                lastItalic = c.IsItalic;
                lastUnderline = c.IsUnderline;
                lastStrike = c.IsStrikeThrough;
                stylesActive = true;
            }

            sb.Append(c.Char);
        }

        if (stylesActive)
        {
            sb.Append(ESCAPE);
            sb.Append(RESET);
        }

        return sb.ToString();
    }
}

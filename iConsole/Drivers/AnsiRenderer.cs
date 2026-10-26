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
    private const string OSC = "\x1B]";
    private const string STRING_TERMINATOR = "\x1B\\";

    /// <summary>
    /// Whether OSC 8 hyperlinks are written. They are on unless <c>ITERMINAL_HYPERLINKS=0</c> is set or the terminal
    /// is the Linux console or a dumb terminal, which would print the sequence's text. A terminal that does not know
    /// OSC 8 ignores it, so the default is to send it.
    /// </summary>
    public static bool HyperlinksEnabled { get; set; } = DetectHyperlinkSupport();

    private static bool DetectHyperlinkSupport()
    {
        if (Environment.GetEnvironmentVariable("ITERMINAL_HYPERLINKS") == "0")
        {
            return false;
        }

        string? term = Environment.GetEnvironmentVariable("TERM");
        return term is not ("linux" or "dumb");
    }

    // ANSI palette index of each Color ordinal, for the 58:5:n underline colour (the SGR fg codes above are not an index).
    private static readonly byte[] s_paletteIndex =
    [
        0,  // Black
        4,  // DarkBlue
        2,  // DarkGreen
        6,  // DarkCyan
        1,  // DarkRed
        5,  // DarkMagenta
        3,  // DarkYellow
        7,  // Gray
        8,  // DarkGray
        12, // Blue
        10, // Green
        14, // Cyan
        9,  // Red
        13, // Magenta
        11, // Yellow
        15, // White
    ];

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
        AppendColorAndStyle(stringBuilder, conxel, reset: false);
        ushort openLink = AppendHyperlink(stringBuilder, Hyperlinks.None, conxel.LinkId);
        stringBuilder.Append(conxel.Char);
        AppendHyperlink(stringBuilder, openLink, Hyperlinks.None);
        stringBuilder.Append(ESCAPE);
        stringBuilder.Append(RESET);
        return stringBuilder;
    }

    /// <summary>True when drawing <paramref name="conxel"/> needs more than the console's own colour calls.</summary>
    public static bool NeedsAnsi(in Conxel conxel)
    {
        return HasAttributes(conxel) || (conxel.LinkId != Hyperlinks.None && HyperlinksEnabled);
    }

    /// <summary>
    /// Emits <c>CSI [0;]fg;bg[;styles]m</c> into <paramref name="sb"/>. <paramref name="reset"/> clears what the previous
    /// cell turned on first: the sequence only ever adds attributes, so without it bold would stay on after a bold cell.
    /// </summary>
    private static void AppendColorAndStyle(StringBuilder sb, in Conxel conxel, bool reset)
    {
        sb.Append(CSI);
        if (reset)
        {
            sb.Append('0');
            sb.Append(SEMICOLON);
        }
        sb.Append(s_fgCodes[(int)conxel.ForegroundColor]);
        sb.Append(SEMICOLON);
        sb.Append(s_bgCodes[(int)conxel.BackgroundColor]);
        if (conxel.IsBold)          { sb.Append(SEMICOLON); sb.Append(BOLD); }
        if (conxel.IsDim)           { sb.Append(SEMICOLON); sb.Append('2'); }
        if (conxel.IsItalic)        { sb.Append(SEMICOLON); sb.Append(ITALIC); }
        if (conxel.IsBlink)         { sb.Append(SEMICOLON); sb.Append(BLINK); }
        if (conxel.IsReverse)       { sb.Append(SEMICOLON); sb.Append('7'); }
        if (conxel.IsStrikeThrough) { sb.Append(SEMICOLON); sb.Append(STRIKE_THROUGH); }
        if (conxel.IsOverline)      { sb.Append(SEMICOLON); sb.Append("53"); }
        if (conxel.IsUnderline)
        {
            sb.Append(SEMICOLON);
            sb.Append(UNDERLINE);
            // Sub-parameters: a terminal that does not know the style falls back to a plain underline.
            switch (conxel.UnderlineStyle)
            {
                case UnderlineStyle.Double: sb.Append(":2"); break;
                case UnderlineStyle.Curly:  sb.Append(":3"); break;
                case UnderlineStyle.Dotted: sb.Append(":4"); break;
                case UnderlineStyle.Dashed: sb.Append(":5"); break;
            }

            if (conxel.HasUnderlineColor)
            {
                sb.Append(";58:5:");
                sb.Append(s_paletteIndex[(int)conxel.UnderlineColor]);
            }
        }
        sb.Append(END);
    }

    private static bool HasAttributes(in Conxel c)
    {
        return c.IsBold || c.IsItalic || c.IsUnderline || c.IsStrikeThrough
            || c.IsDim || c.IsReverse || c.IsBlink || c.IsOverline;
    }

    private static bool SameStyle(in Conxel a, in Conxel b)
    {
        return a.ForegroundColor == b.ForegroundColor && a.BackgroundColor == b.BackgroundColor
            && a.IsBold == b.IsBold && a.IsItalic == b.IsItalic && a.IsUnderline == b.IsUnderline
            && a.IsStrikeThrough == b.IsStrikeThrough && a.IsDim == b.IsDim && a.IsReverse == b.IsReverse
            && a.IsBlink == b.IsBlink && a.IsOverline == b.IsOverline
            && (!a.IsUnderline || (a.UnderlineStyle == b.UnderlineStyle
                && a.HasUnderlineColor == b.HasUnderlineColor
                && (!a.HasUnderlineColor || a.UnderlineColor == b.UnderlineColor)));
    }

    /// <summary>
    /// Switches from link <paramref name="from"/> to link <paramref name="to"/> (<see cref="Hyperlinks.None"/> for none):
    /// <c>OSC 8 ; id=… ; target ST</c> opens one and an empty target closes it. The id makes a link that is written in
    /// several pieces, such as one wrapped over two rows, a single link to the terminal. Returns the link now open.
    /// </summary>
    private static ushort AppendHyperlink(StringBuilder sb, ushort from, ushort to)
    {
        if (!HyperlinksEnabled || from == to)
        {
            return from;
        }

        string? target = Hyperlinks.Resolve(to);
        if (target is null)
        {
            if (from != Hyperlinks.None)
            {
                sb.Append(OSC).Append("8;;").Append(STRING_TERMINATOR);
            }

            return Hyperlinks.None;
        }

        sb.Append(OSC).Append("8;id=iT").Append(to).Append(';').Append(target).Append(STRING_TERMINATOR);
        return to;
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

        Conxel last = default;
        bool haveLast = false;
        bool attributesOn = false;
        ushort openLink = Hyperlinks.None;

        foreach (var c in list)
        {
            if (!haveLast || !SameStyle(c, last))
            {
                AppendColorAndStyle(sb, c, reset: attributesOn);
                attributesOn = HasAttributes(c);
                last = c;
                haveLast = true;
            }

            openLink = AppendHyperlink(sb, openLink, c.LinkId);
            sb.Append(c.Char);
        }

        // A link must not stay open into whatever is written next.
        AppendHyperlink(sb, openLink, Hyperlinks.None);

        if (haveLast)
        {
            sb.Append(ESCAPE);
            sb.Append(RESET);
        }

        return sb.ToString();
    }
}

using System.Text;
using System.Text.RegularExpressions;
using iConsole.Display;

namespace iConsole.Helper;

/// <summary>A stretch of a log line in its own style. Null colours leave the line's own.</summary>
public sealed record LogSpan(int Start, int Length, Color? Foreground = null, Color? Background = null, bool Bold = false, bool Underline = false, bool Italic = false)
{
    public int End => Start + Length;

    public bool Overlaps(int start, int end) => Start < end && start < End;
}

/// <summary>A pattern that colours what it finds in every log line, such as a URL.</summary>
public sealed class LogHighlighter
{
    public LogHighlighter(Regex pattern, Color? foreground, bool bold = false, bool underline = false)
    {
        Pattern = pattern;
        Foreground = foreground;
        Bold = bold;
        Underline = underline;
    }

    public Regex Pattern { get; }

    public Color? Foreground { get; }

    public bool Bold { get; }

    public bool Underline { get; }

    /// <summary>A highlighter for <paramref name="pattern"/>, with a match timeout so a bad pattern cannot hold the log up.</summary>
    public static LogHighlighter Create(string pattern, Color? foreground, bool bold = false, bool underline = false, RegexOptions options = RegexOptions.None) =>
        new(new Regex(pattern, options | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)), foreground, bold, underline);
}

/// <summary>The highlighters a <c>LogView</c> starts with.</summary>
public static class LogHighlighters
{
    /// <summary>
    /// Error words, warning words, ISO timestamps, URLs, Windows and Unix file paths (with a <c>:line</c> after them), quoted text
    /// and numbers. Earlier ones win where two overlap, so a number inside a URL stays part of the URL.
    /// </summary>
    public static IReadOnlyList<LogHighlighter> Default { get; } =
    [
        LogHighlighter.Create(@"\b(?:ERROR|FATAL|FAIL(?:ED|URE)?|EXCEPTION|CRITICAL|PANIC)\b", Color.Red, bold: true, options: RegexOptions.IgnoreCase),
        LogHighlighter.Create(@"\b(?:WARN(?:ING)?)\b", Color.Yellow, bold: true, options: RegexOptions.IgnoreCase),
        LogHighlighter.Create(@"\b\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:[.,]\d+)?(?:Z|[+-]\d{2}:?\d{2})?", Color.Gray),
        LogHighlighter.Create(@"\b(?:https?|ftp|ssh|file)://[^\s<>""')\]]+", Color.Cyan, underline: true),
        LogHighlighter.Create(@"\b[A-Za-z]:\\(?:[^\s\\/:*?""<>|]+\\)*[^\s\\/:*?""<>|]+(?::\d+(?::\d+)?)?", Color.Green),
        LogHighlighter.Create(@"(?<![\w:/.])/(?:[\w.@+-]+/)+[\w.@+-]+(?::\d+(?::\d+)?)?", Color.Green),
        LogHighlighter.Create(@"""[^""\r\n]*""", Color.Yellow),
        LogHighlighter.Create(@"(?<![\w.])-?\d+(?:\.\d+)?(?![\w.])", Color.Magenta),
    ];

    /// <summary>The longest line that is highlighted; a longer one is left plain so a huge line cannot slow the log.</summary>
    public const int MaxLineLength = 4000;

    /// <summary>The spans <paramref name="highlighters"/> find in <paramref name="text"/> that do not overlap <paramref name="taken"/>.</summary>
    public static IEnumerable<LogSpan> Find(string text, IEnumerable<LogHighlighter> highlighters, IReadOnlyList<LogSpan> taken)
    {
        if (text.Length == 0 || text.Length > MaxLineLength)
        {
            yield break;
        }

        var added = new List<LogSpan>();
        foreach (LogHighlighter highlighter in highlighters)
        {
            MatchCollection matches;
            try
            {
                matches = highlighter.Pattern.Matches(text);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            foreach (Match match in matches)
            {
                int start = match.Index;
                int end = match.Index + match.Length;
                if (match.Length == 0 || taken.Any(t => t.Overlaps(start, end)) || added.Any(a => a.Overlaps(start, end)))
                {
                    continue;
                }

                var span = new LogSpan(start, match.Length, highlighter.Foreground, null, highlighter.Bold, highlighter.Underline);
                added.Add(span);
                yield return span;
            }
        }
    }
}

/// <summary>
/// Reads the ANSI escape codes that build tools, git and docker put in their output: colours and bold, italic and underline
/// become <see cref="LogSpan"/>s over the plain text, and every other control sequence (moving the cursor, erasing, titles) is
/// dropped. 256-colour and true-colour codes are mapped to the nearest of the 16 colours.
/// </summary>
public static class AnsiText
{
    private static readonly Color[] Standard =
    [
        Color.Black, Color.DarkRed, Color.DarkGreen, Color.DarkYellow, Color.DarkBlue, Color.DarkMagenta, Color.DarkCyan, Color.Gray,
    ];

    private static readonly Color[] Bright =
    [
        Color.DarkGray, Color.Red, Color.Green, Color.Yellow, Color.Blue, Color.Magenta, Color.Cyan, Color.White,
    ];

    private sealed class Style
    {
        public Color? Foreground;
        public Color? Background;
        public bool Bold;
        public bool Italic;
        public bool Underline;

        public bool IsPlain => Foreground is null && Background is null && !Bold && !Italic && !Underline;
    }

    /// <summary>True when <paramref name="text"/> has an escape character, so parsing it is worth doing.</summary>
    public static bool HasEscapes(string text) => text.Contains('\u001b');

    /// <summary>The text without its escape sequences and the style spans over it, in positions of the plain text.</summary>
    public static (string Plain, IReadOnlyList<LogSpan> Spans) Parse(string text)
    {
        if (!HasEscapes(text))
        {
            return (text, []);
        }

        var plain = new StringBuilder(text.Length);
        var spans = new List<LogSpan>();
        var style = new Style();
        int runStart = 0;

        void Close()
        {
            if (!style.IsPlain && plain.Length > runStart)
            {
                spans.Add(new LogSpan(runStart, plain.Length - runStart, style.Foreground, style.Background, style.Bold, style.Underline, style.Italic));
            }

            runStart = plain.Length;
        }

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c != '\u001b')
            {
                plain.Append(c);
                i++;
                continue;
            }

            if (i + 1 >= text.Length)
            {
                break;
            }

            char kind = text[i + 1];
            if (kind == '[')
            {
                int end = i + 2;
                while (end < text.Length && text[end] is >= (char)0x30 and <= (char)0x3F)
                {
                    end++;
                }

                while (end < text.Length && text[end] is >= (char)0x20 and <= (char)0x2F)
                {
                    end++;
                }

                if (end >= text.Length)
                {
                    break;
                }

                if (text[end] == 'm')
                {
                    Close();
                    Apply(text.Substring(i + 2, end - i - 2), style);
                }

                i = end + 1;
            }
            else if (kind == ']')
            {
                // An operating system command (a title, a hyperlink) runs to BEL or to ESC \.
                int end = i + 2;
                while (end < text.Length && text[end] != '\u0007' && !(text[end] == '\u001b' && end + 1 < text.Length && text[end + 1] == '\\'))
                {
                    end++;
                }

                i = end < text.Length && text[end] == '\u001b' ? end + 2 : end + 1;
            }
            else
            {
                i += 2;
            }
        }

        Close();
        return (plain.ToString(), spans);
    }

    private static void Apply(string parameters, Style style)
    {
        int[] codes = parameters.Length == 0 ? [0] : [.. parameters.Split(';', ':').Select(p => int.TryParse(p, out int value) ? value : 0)];
        for (int i = 0; i < codes.Length; i++)
        {
            int code = codes[i];
            switch (code)
            {
                case 0:
                    style.Foreground = null;
                    style.Background = null;
                    style.Bold = style.Italic = style.Underline = false;
                    break;
                case 1: style.Bold = true; break;
                case 3: style.Italic = true; break;
                case 4: style.Underline = true; break;
                case 22: style.Bold = false; break;
                case 23: style.Italic = false; break;
                case 24: style.Underline = false; break;
                case 39: style.Foreground = null; break;
                case 49: style.Background = null; break;
                case >= 30 and <= 37: style.Foreground = Standard[code - 30]; break;
                case >= 90 and <= 97: style.Foreground = Bright[code - 90]; break;
                case >= 40 and <= 47: style.Background = Standard[code - 40]; break;
                case >= 100 and <= 107: style.Background = Bright[code - 100]; break;
                case 38 or 48:
                    Color? color = null;
                    if (i + 2 < codes.Length && codes[i + 1] == 5)
                    {
                        color = FromIndex(codes[i + 2]);
                        i += 2;
                    }
                    else if (i + 4 < codes.Length && codes[i + 1] == 2)
                    {
                        color = Nearest(codes[i + 2], codes[i + 3], codes[i + 4]);
                        i += 4;
                    }

                    if (color is not null)
                    {
                        if (code == 38)
                        {
                            style.Foreground = color;
                        }
                        else
                        {
                            style.Background = color;
                        }
                    }

                    break;
            }
        }
    }

    /// <summary>The nearest of the 16 colours to an index in the 256-colour palette.</summary>
    public static Color FromIndex(int index)
    {
        index = Math.Clamp(index, 0, 255);
        if (index < 8)
        {
            return Standard[index];
        }

        if (index < 16)
        {
            return Bright[index - 8];
        }

        if (index < 232)
        {
            int n = index - 16;
            int[] levels = [0, 95, 135, 175, 215, 255];
            return Nearest(levels[n / 36], levels[(n / 6) % 6], levels[n % 6]);
        }

        int gray = 8 + ((index - 232) * 10);
        return Nearest(gray, gray, gray);
    }

    /// <summary>The nearest of the 16 colours to an RGB value.</summary>
    public static Color Nearest(int r, int g, int b)
    {
        (Color Color, int R, int G, int B)[] palette =
        [
            (Color.Black, 0, 0, 0), (Color.DarkRed, 128, 0, 0), (Color.DarkGreen, 0, 128, 0), (Color.DarkYellow, 128, 128, 0),
            (Color.DarkBlue, 0, 0, 128), (Color.DarkMagenta, 128, 0, 128), (Color.DarkCyan, 0, 128, 128), (Color.Gray, 192, 192, 192),
            (Color.DarkGray, 128, 128, 128), (Color.Red, 255, 0, 0), (Color.Green, 0, 255, 0), (Color.Yellow, 255, 255, 0),
            (Color.Blue, 0, 0, 255), (Color.Magenta, 255, 0, 255), (Color.Cyan, 0, 255, 255), (Color.White, 255, 255, 255),
        ];
        return palette.OrderBy(p => ((p.R - r) * (p.R - r)) + ((p.G - g) * (p.G - g)) + ((p.B - b) * (p.B - b))).First().Color;
    }
}

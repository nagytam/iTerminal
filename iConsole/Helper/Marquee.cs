namespace iConsole.Helper;

/// <summary>
/// Scrolling text for a title that is longer than the room it has: a window onto the text that rests at the start, slides
/// left one cell at a time, wraps round past a gap and rests again. Pure functions of time, so a test sets the time.
/// </summary>
public static class Marquee
{
    /// <summary>How long the text rests at its start before it moves.</summary>
    public static readonly TimeSpan DefaultPause = TimeSpan.FromMilliseconds(1500);

    /// <summary>How long it takes to move one cell.</summary>
    public static readonly TimeSpan DefaultStep = TimeSpan.FromMilliseconds(140);

    /// <summary>Spaces between the end of the text and its start coming round again.</summary>
    public const int DefaultGap = 4;

    /// <summary>
    /// How far the text has moved after <paramref name="elapsed"/>: 0 while it rests, then one more for each step, round to 0
    /// again after the text and the gap have gone past. 0 for text that fits.
    /// </summary>
    public static int Offset(int textLength, int width, TimeSpan elapsed, TimeSpan? pause = null, TimeSpan? step = null, int gap = DefaultGap)
    {
        if (width <= 0 || textLength <= width)
        {
            return 0;
        }

        TimeSpan rest = pause ?? DefaultPause;
        double stepMilliseconds = Math.Max(1, (step ?? DefaultStep).TotalMilliseconds);
        int loop = textLength + Math.Max(0, gap);
        double period = rest.TotalMilliseconds + (loop * stepMilliseconds);
        double t = elapsed.TotalMilliseconds < 0 ? 0 : elapsed.TotalMilliseconds % period;
        return t < rest.TotalMilliseconds ? 0 : (int)((t - rest.TotalMilliseconds) / stepMilliseconds);
    }

    /// <summary>
    /// The <paramref name="width"/> characters of <paramref name="text"/> to show after <paramref name="elapsed"/>. Text that fits
    /// is padded and never moves; longer text moves as <see cref="Offset"/> says.
    /// </summary>
    public static string Window(string? text, int width, TimeSpan elapsed, TimeSpan? pause = null, TimeSpan? step = null, int gap = DefaultGap)
    {
        text ??= string.Empty;
        if (width <= 0)
        {
            return string.Empty;
        }

        if (text.Length <= width)
        {
            return text.PadRight(width);
        }

        string loop = text + new string(' ', Math.Max(0, gap));
        int offset = Offset(text.Length, width, elapsed, pause, step, gap) % loop.Length;
        string doubled = loop + loop;
        return doubled.Substring(offset, width);
    }
}

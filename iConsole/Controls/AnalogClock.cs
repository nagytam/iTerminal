using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A round clock face: twelve ticks and hour, minute and (optionally) second hands, drawn with characters.
/// The owner sets <see cref="TimeOfDay"/> before a draw, so the face shows whatever clock the owner reads
/// (a world-clock zone, the local time) and nothing here reads the time itself.
///
/// <para>A terminal cell is about twice as tall as it is wide, so the geometry works in physical units where a
/// row is two units high. The face is as large as the smaller of its two half-extents allows, and a hand is
/// walked from the centre outwards one half-cell at a time, drawn with the character that matches its slope.</para>
/// </summary>
public class AnalogClock : Control
{
    private const double HourHandLength = 0.5;
    private const double MinuteHandLength = 0.8;
    private const double SecondHandLength = 0.9;
    private const double TickRadius = 0.95;

    // Hands are drawn hour first, so the minute hand covers it where they cross, and the second hand is on top.

    public AnalogClock(IThemeService themeService)
        : base(themeService)
    {
        ViewSize = new Size(30, 15);
    }

    /// <summary>The time the hands show, as the time since midnight. Values beyond a day wrap.</summary>
    public TimeSpan TimeOfDay { get; set; }

    /// <summary>Whether the second hand is drawn. A clock that only needs the hour and minute can turn it off.</summary>
    public bool ShowSeconds { get; set; } = true;

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        IReadOnlyList<string> rows = Render(width, (int)ViewSize.Height, TimeOfDay, ShowSeconds);
        for (int row = 0; row < rows.Count; row++)
        {
            buffer.Write(
                ZOrder,
                new Position(AbsolutePosition.X, AbsolutePosition.Y + row),
                new Size(width, 1),
                new ConxelString(rows[row], colorScheme.Foreground, colorScheme.Background));
        }
    }

    /// <summary>
    /// The face as one string per row, <paramref name="width"/> characters wide. Static so a page that draws its own rows can
    /// place the face without a child control, and so a test can read the rows without a screen.
    /// </summary>
    public static IReadOnlyList<string> Render(int width, int height, TimeSpan timeOfDay, bool showSeconds)
    {
        char[][] canvas = new char[Math.Max(0, height)][];
        for (int row = 0; row < canvas.Length; row++)
        {
            canvas[row] = new string(' ', Math.Max(0, width)).ToCharArray();
        }

        // Physical half-extents: a column is one unit wide, a row two units tall.
        double centreX = (width - 1) / 2.0;
        double centreY = (height - 1) / 2.0;
        double radius = Math.Min(centreX, centreY * 2) * 0.95;
        if (radius >= 1)
        {
            for (int tick = 0; tick < 12; tick++)
            {
                double angle = tick * Math.PI / 6;
                Put(canvas, centreX + (Math.Sin(angle) * radius * TickRadius), centreY - (Math.Cos(angle) * radius * TickRadius / 2), tick % 3 == 0 ? '+' : '·');
            }

            double seconds = timeOfDay.TotalSeconds % 86400;
            if (seconds < 0)
            {
                seconds += 86400;
            }

            Hand(canvas, centreX, centreY, radius * HourHandLength, seconds / 43200 * 2 * Math.PI, '#');
            Hand(canvas, centreX, centreY, radius * MinuteHandLength, seconds / 3600 * 2 * Math.PI, null);
            if (showSeconds)
            {
                Hand(canvas, centreX, centreY, radius * SecondHandLength, seconds / 60 * 2 * Math.PI, '.');
            }

            Put(canvas, centreX, centreY, '•');
        }

        return canvas.Select(row => new string(row)).ToList();
    }

    /// <summary>Walks one hand from the centre to its tip. <paramref name="glyph"/> null draws the slope's own character.</summary>
    private static void Hand(char[][] canvas, double centreX, double centreY, double length, double angle, char? glyph)
    {
        // Clock angles: zero is twelve o'clock and the hand turns clockwise, so x grows with sin and y falls with cos.
        double dx = Math.Sin(angle);
        double dy = -Math.Cos(angle);
        for (double step = 0; step <= length; step += 0.5)
        {
            Put(canvas, centreX + (dx * step), centreY + (dy * step / 2), glyph ?? SlopeGlyph(dx, dy));
        }
    }

    /// <summary>The character that runs along a line of this slope, with the cell's aspect taken into account.</summary>
    private static char SlopeGlyph(double dx, double dy)
    {
        double horizontal = Math.Abs(dx);
        double vertical = Math.Abs(dy) / 2;
        if (vertical > horizontal * 2)
        {
            return '|';
        }

        if (horizontal > vertical * 2)
        {
            return '-';
        }

        // Up-right and down-left share a sign; screen rows grow downwards, so the diagonal is '/' when the signs differ.
        return dx * dy < 0 ? '/' : '\\';
    }

    private static void Put(char[][] canvas, double x, double y, char glyph)
    {
        int column = (int)Math.Round(x);
        int row = (int)Math.Round(y);
        if (row < 0 || row >= canvas.Length || column < 0 || column >= canvas[row].Length)
        {
            return;
        }

        canvas[row][column] = glyph;
    }
}

using iConsole.Display;

namespace iTerminal.Core.ScreenSavers;

/// <summary>
/// The time in large characters, and the date under it, bouncing round the screen. It turns at each edge and takes a new colour
/// each time it does. A canvas too small for the clock shows nothing rather than a clipped one.
/// </summary>
public sealed class ClockEffect : IScreenSaverEffect
{
    private const int GlyphHeight = 5;
    private const int GlyphWidth = 3;
    private const int GlyphGap = 1;
    private const int DateRows = 2;

    private static readonly Color[] Palette =
    [
        Color.Cyan, Color.Green, Color.Yellow, Color.Magenta, Color.Red, Color.Blue, Color.White,
    ];

    /// <summary>The five rows of each character, drawn with block characters.</summary>
    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['0'] = ["███", "█ █", "█ █", "█ █", "███"],
        ['1'] = [" █ ", "██ ", " █ ", " █ ", "███"],
        ['2'] = ["███", "  █", "███", "█  ", "███"],
        ['3'] = ["███", "  █", "███", "  █", "███"],
        ['4'] = ["█ █", "█ █", "███", "  █", "  █"],
        ['5'] = ["███", "█  ", "███", "  █", "███"],
        ['6'] = ["███", "█  ", "███", "█ █", "███"],
        ['7'] = ["███", "  █", "  █", "  █", "  █"],
        ['8'] = ["███", "█ █", "███", "█ █", "███"],
        ['9'] = ["███", "█ █", "███", "  █", "███"],
        [':'] = ["   ", " █ ", "   ", " █ ", "   "],
    };

    private double _x;
    private double _y;
    private double _dx = 0.6;
    private double _dy = 0.35;
    private int _colour;
    private int _width = -1;
    private int _height = -1;

    public void Step(ScreenSaverCanvas canvas, DateTime now)
    {
        string time = now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        string date = now.ToString("yyyy-MM-dd dddd", System.Globalization.CultureInfo.CurrentCulture);
        int clockWidth = WidthOf(time);
        int blockHeight = GlyphHeight + DateRows;
        if (canvas.Width < clockWidth || canvas.Height < blockHeight)
        {
            return;
        }

        if (canvas.Width != _width || canvas.Height != _height)
        {
            _width = canvas.Width;
            _height = canvas.Height;
            _x = (canvas.Width - clockWidth) / 2.0;
            _y = (canvas.Height - blockHeight) / 2.0;
        }

        Move(canvas.Width - clockWidth, canvas.Height - blockHeight);
        Color colour = Palette[_colour];
        DrawText(canvas, time, (int)Math.Round(_x), (int)Math.Round(_y), colour);
        int dateColumn = (int)Math.Round(_x) + ((clockWidth - date.Length) / 2);
        if (dateColumn < 0)
        {
            dateColumn = 0;
        }

        DrawPlain(canvas, date, dateColumn, (int)Math.Round(_y) + GlyphHeight + 1, colour);
    }

    private void Move(int maxX, int maxY)
    {
        _x += _dx;
        _y += _dy;
        bool bounced = false;
        if (_x < 0 || _x > maxX)
        {
            _dx = -_dx;
            _x = Math.Clamp(_x, 0, Math.Max(0, maxX));
            bounced = true;
        }

        if (_y < 0 || _y > maxY)
        {
            _dy = -_dy;
            _y = Math.Clamp(_y, 0, Math.Max(0, maxY));
            bounced = true;
        }

        if (bounced)
        {
            _colour = (_colour + 1) % Palette.Length;
        }
    }

    private static int WidthOf(string text) => (text.Length * (GlyphWidth + GlyphGap)) - GlyphGap;

    private static void DrawText(ScreenSaverCanvas canvas, string text, int left, int top, Color colour)
    {
        int column = left;
        foreach (char character in text)
        {
            if (!Font.TryGetValue(character, out string[]? rows))
            {
                continue;
            }

            for (int row = 0; row < rows.Length; row++)
            {
                for (int i = 0; i < rows[row].Length; i++)
                {
                    if (rows[row][i] != ' ')
                    {
                        canvas.Put(column + i, top + row, rows[row][i], colour, isBold: true);
                    }
                }
            }

            column += GlyphWidth + GlyphGap;
        }
    }

    private static void DrawPlain(ScreenSaverCanvas canvas, string text, int left, int top, Color colour)
    {
        for (int i = 0; i < text.Length; i++)
        {
            canvas.Put(left + i, top, text[i], colour);
        }
    }
}

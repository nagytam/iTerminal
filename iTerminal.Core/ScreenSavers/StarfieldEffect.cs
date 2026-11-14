using iConsole.Display;

namespace iTerminal.Core.ScreenSavers;

/// <summary>
/// Stars flying out of the middle of the screen towards the viewer. Each star is a point in a cube in front of the eye: it
/// comes nearer each frame and is drawn where its position projects, with a character and a brightness that grow as it nears.
/// A star that leaves the screen, or reaches the eye, is started again far away.
/// </summary>
public sealed class StarfieldEffect : IScreenSaverEffect
{
    /// <summary>How many stars there are.</summary>
    public const int StarCount = 240;

    /// <summary>How far a star comes towards the eye each frame. The eye is at depth zero, a new star starts at depth one.</summary>
    private const double Approach = 0.018;

    private readonly Random _random;
    private double[] _x = [];
    private double[] _y = [];
    private double[] _depth = [];
    private int _width = -1;
    private int _height = -1;

    public StarfieldEffect(Random random)
    {
        _random = random;
    }

    public void Step(ScreenSaverCanvas canvas, DateTime now)
    {
        if (canvas.Width != _width || canvas.Height != _height)
        {
            Restart(canvas.Width, canvas.Height);
        }

        double halfWidth = canvas.Width / 2.0;
        double halfHeight = canvas.Height / 2.0;
        for (int i = 0; i < StarCount; i++)
        {
            _depth[i] -= Approach;
            if (_depth[i] <= Approach)
            {
                Spawn(i);
            }

            // Perspective: a point at (x, y, depth) lands at x / depth from the middle.
            double screenX = halfWidth + (_x[i] / _depth[i] * halfWidth);
            double screenY = halfHeight + (_y[i] / _depth[i] * halfHeight);
            int column = (int)Math.Floor(screenX);
            int row = (int)Math.Floor(screenY);
            if (column < 0 || row < 0 || column >= canvas.Width || row >= canvas.Height)
            {
                Spawn(i);
                continue;
            }

            // Brightness rises as the star nears: far stars are faint dots, near ones are bold stars.
            double nearness = 1 - _depth[i];
            (char glyph, Color colour, bool isBold) = nearness switch
            {
                > 0.85 => ('#', Color.White, true),
                > 0.6 => ('*', Color.White, false),
                > 0.35 => ('+', Color.Gray, false),
                _ => ('.', Color.DarkGray, false),
            };
            canvas.Put(column, row, glyph, colour, isBold);
        }
    }

    private void Restart(int width, int height)
    {
        _width = width;
        _height = height;
        _x = new double[StarCount];
        _y = new double[StarCount];
        _depth = new double[StarCount];
        for (int i = 0; i < StarCount; i++)
        {
            Spawn(i);
            // Spread the stars through their life, so the first frame is not a ring of new points at the edge.
            _depth[i] = 0.05 + (_random.NextDouble() * 0.95);
        }
    }

    private void Spawn(int index)
    {
        _x[index] = (_random.NextDouble() * 2) - 1;
        _y[index] = (_random.NextDouble() * 2) - 1;
        _depth[index] = 1;
    }
}

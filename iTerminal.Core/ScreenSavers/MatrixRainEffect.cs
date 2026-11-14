using iConsole.Display;

namespace iTerminal.Core.ScreenSavers;

/// <summary>
/// Columns of characters falling down the screen, each with a bright head and a green trail that fades to dark green. The
/// characters in a trail change as it falls, which is what gives the effect its flicker. A column that has fallen off the
/// bottom starts again from above the top, after a random pause.
/// </summary>
public sealed class MatrixRainEffect : IScreenSaverEffect
{
    private const string Glyphs = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789@#$%&*+=<>?/|:;";

    /// <summary>A column every this many cells, so the rain is not a solid wall.</summary>
    private const int ColumnSpacing = 2;

    private readonly Random _random;
    private double[] _head = [];
    private double[] _speed = [];
    private int[] _length = [];
    private int _width = -1;
    private int _height = -1;

    public MatrixRainEffect(Random random)
    {
        _random = random;
    }

    public void Step(ScreenSaverCanvas canvas, DateTime now)
    {
        if (canvas.Width != _width || canvas.Height != _height)
        {
            Restart(canvas.Width, canvas.Height);
        }

        for (int column = 0; column < _head.Length; column++)
        {
            _head[column] += _speed[column];
            if (_head[column] - _length[column] > canvas.Height)
            {
                StartColumn(column, fromAbove: true);
            }

            int x = column * ColumnSpacing;
            for (int trail = 0; trail < _length[column]; trail++)
            {
                int y = (int)Math.Floor(_head[column]) - trail;
                if (y < 0 || y >= canvas.Height)
                {
                    continue;
                }

                char glyph = Glyphs[_random.Next(Glyphs.Length)];
                if (trail == 0)
                {
                    canvas.Put(x, y, glyph, Color.White, isBold: true);
                }
                else if (trail < 3)
                {
                    canvas.Put(x, y, glyph, Color.Green);
                }
                else
                {
                    canvas.Put(x, y, glyph, Color.DarkGreen);
                }
            }
        }
    }

    private void Restart(int width, int height)
    {
        _width = width;
        _height = height;
        int columns = (width + ColumnSpacing - 1) / ColumnSpacing;
        _head = new double[columns];
        _speed = new double[columns];
        _length = new int[columns];
        for (int column = 0; column < columns; column++)
        {
            StartColumn(column, fromAbove: false);
        }
    }

    private void StartColumn(int column, bool fromAbove)
    {
        int height = Math.Max(1, _height);
        // Above the top edge, so the trail enters from the top. At first the columns are spread over the whole height.
        _head[column] = fromAbove ? -_random.Next(1, height) : _random.Next(0, height);
        _speed[column] = 0.3 + (_random.NextDouble() * 0.7);
        _length[column] = _random.Next(6, 18);
    }
}

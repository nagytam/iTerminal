using iConsole.Display;

namespace iTerminal.Core.Model.Map;

/// <summary>One character of the drawn map.</summary>
public readonly record struct MapCell(char Char, Color Foreground, bool Bold = false);

/// <summary>
/// Turns map-tile pixels into ASCII art. Each character stands for a block of <see cref="CellWidth"/> ×
/// <see cref="CellHeight"/> map pixels (twice as tall as wide, like a terminal cell). The block's average
/// colour says what it is — water, green space, or land — and how much the pixels inside it vary says how
/// much is drawn there (a road, a building edge, a label), which picks the character from a density ramp.
/// </summary>
public static class MapAsciiRenderer
{
    public const int CellWidth = 4;
    public const int CellHeight = 8;

    /// <summary>From "nothing there" to "solid", chosen by how much the block varies.</summary>
    public const string Ramp = " .:-=+*#%@";

    private static readonly (double Detail, char Char)[] Steps =
    [
        (3, ' '), (7, '.'), (12, ':'), (18, '-'), (25, '='), (33, '+'), (42, '*'), (52, '#'), (64, '%'),
    ];

    /// <summary>
    /// Renders the block of <paramref name="tile"/> whose top-left corner is (<paramref name="u"/>,
    /// <paramref name="v"/>) and which is <paramref name="width"/> × <paramref name="height"/>, all in the tile's
    /// 256 × 256 coordinate space (fractions are fine: a block of a zoomed-out parent tile is smaller than a pixel).
    /// </summary>
    public static MapCell Render(MapTile tile, double u, double v, double width, double height)
    {
        var scaleX = tile.Width / (double)MapProjection.TileSize;
        var scaleY = tile.Height / (double)MapProjection.TileSize;
        var samplesX = Math.Clamp((int)Math.Round(width * scaleX), 1, 16);
        var samplesY = Math.Clamp((int)Math.Round(height * scaleY), 1, 16);

        double red = 0, green = 0, blue = 0, lum = 0, lumSquares = 0;
        int warm = 0, count = 0;
        for (var j = 0; j < samplesY; j++)
        {
            var py = Math.Clamp((int)((v + (j + 0.5) * height / samplesY) * scaleY), 0, tile.Height - 1);
            for (var i = 0; i < samplesX; i++)
            {
                var px = Math.Clamp((int)((u + (i + 0.5) * width / samplesX) * scaleX), 0, tile.Width - 1);
                var at = (py * tile.Width + px) * 4;
                int r = tile.Rgba[at], g = tile.Rgba[at + 1], b = tile.Rgba[at + 2];
                red += r;
                green += g;
                blue += b;
                var l = 0.299 * r + 0.587 * g + 0.114 * b;
                lum += l;
                lumSquares += l * l;
                if (r > 225 && r - b > 40 && b < 215)
                {
                    warm++;
                }

                count++;
            }
        }

        red /= count;
        green /= count;
        blue /= count;
        lum /= count;
        var detail = Math.Sqrt(Math.Max(0, lumSquares / count - lum * lum));

        var isWater = blue - red > 25 && blue >= green;
        var isGreen = !isWater && green > red + 4 && green > blue + 8;
        var isRoad = warm * 4 >= count;

        var baseChar = isWater ? '~' : isGreen ? '"' : ' ';
        var color = isWater ? Color.Blue : isGreen ? Color.Green : Color.Gray;
        var ink = ' ';
        foreach (var (limit, c) in Steps)
        {
            ink = c;
            if (detail < limit)
            {
                break;
            }

            ink = '@';
        }

        // A flat block shows what it is made of; a busy one shows the strokes in it.
        var character = ink == ' ' ? baseChar : ink;
        if (ink != ' ' && !isWater && !isGreen)
        {
            color = lum < 170 ? Color.DarkGray : Color.White;
        }

        if (isRoad)
        {
            color = Color.Yellow;
            if (character is ' ' or '"')
            {
                character = '=';
            }
        }

        return new MapCell(character, color, detail >= 33 || isRoad);
    }
}

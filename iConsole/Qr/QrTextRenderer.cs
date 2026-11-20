using System.Text;

namespace iConsole.Qr;

/// <summary>
/// Draws a QR module matrix as text: two module rows per line, with the half-block characters
/// <c>▀ ▄ █</c> for dark modules and spaces for light ones, and a light quiet zone around the code.
/// A dark module is drawn as the foreground glyph, so a caller that needs a dark code on a light
/// background paints the cells itself.
/// </summary>
public static class QrTextRenderer
{
    public const int DefaultQuietZone = 4;

    public static IReadOnlyList<string> Render(bool[,] modules, int quietZone = DefaultQuietZone)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentOutOfRangeException.ThrowIfNegative(quietZone);

        int size = modules.GetLength(0);
        int total = size + 2 * quietZone;

        bool Dark(int x, int y)
        {
            int moduleX = x - quietZone;
            int moduleY = y - quietZone;
            return moduleX >= 0 && moduleY >= 0 && moduleX < size && moduleY < size && modules[moduleX, moduleY];
        }

        List<string> lines = [];
        for (int y = 0; y < total; y += 2)
        {
            StringBuilder line = new(total);
            for (int x = 0; x < total; x++)
            {
                bool top = Dark(x, y);
                bool bottom = y + 1 < total && Dark(x, y + 1);
                line.Append(top ? (bottom ? '█' : '▀') : (bottom ? '▄' : ' '));
            }

            lines.Add(line.ToString());
        }

        return lines;
    }
}

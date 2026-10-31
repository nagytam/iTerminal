namespace iConsole.Graphics;

/// <summary>The emblem on the splash screen, drawn from shapes so the app carries no image file: a gradient tile with a prompt on it.</summary>
public static class SplashLogo
{
    /// <summary>Packed RGB, <paramref name="width"/> × <paramref name="height"/> pixels.</summary>
    public static byte[] Render(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        byte[] rgb = new byte[width * height * 3];
        double thickness = Math.Max(2, height * 0.085);
        double w = width;
        double h = height;

        // A chevron and an underscore: the sign of a command prompt.
        (double X1, double Y1, double X2, double Y2)[] strokes =
        [
            (w * 0.30, h * 0.28, w * 0.44, h * 0.50),
            (w * 0.44, h * 0.50, w * 0.30, h * 0.72),
            (w * 0.52, h * 0.74, w * 0.70, h * 0.74),
        ];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double t = (x / w + y / h) / 2;
                double r = 24 + 90 * t;
                double g = 60 - 30 * t;
                double b = 170 + 40 * t;

                double distance = double.MaxValue;
                foreach (var s in strokes)
                {
                    distance = Math.Min(distance, DistanceToSegment(x + 0.5, y + 0.5, s.X1, s.Y1, s.X2, s.Y2));
                }

                // One pixel of soft edge keeps the strokes from looking jagged.
                double cover = Math.Clamp(thickness / 2 + 0.5 - distance, 0, 1);
                r += (245 - r) * cover;
                g += (245 - g) * cover;
                b += (245 - b) * cover;

                // A thin light frame around the tile.
                if (x < 2 || y < 2 || x >= width - 2 || y >= height - 2)
                {
                    r = (r + 255) / 2;
                    g = (g + 255) / 2;
                    b = (b + 255) / 2;
                }

                int o = (y * width + x) * 3;
                rgb[o] = (byte)Math.Clamp(r, 0, 255);
                rgb[o + 1] = (byte)Math.Clamp(g, 0, 255);
                rgb[o + 2] = (byte)Math.Clamp(b, 0, 255);
            }
        }

        return rgb;
    }

    private static double DistanceToSegment(double px, double py, double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double lengthSquared = dx * dx + dy * dy;
        double t = lengthSquared == 0 ? 0 : Math.Clamp(((px - x1) * dx + (py - y1) * dy) / lengthSquared, 0, 1);
        double cx = x1 + t * dx;
        double cy = y1 + t * dy;
        return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }
}

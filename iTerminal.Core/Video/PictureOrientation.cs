namespace iTerminal.Core.Video;

/// <summary>
/// How the picture viewer turns a picture as it shows it: the mirrors first, then clockwise quarter turns. Only the view
/// changes; the file on disk is never written.
/// </summary>
public readonly record struct PictureOrientation(int QuarterTurns, bool FlipHorizontal, bool FlipVertical)
{
    public static PictureOrientation None => new(0, false, false);

    /// <summary>True for a quarter turn either way: the picture's width and height trade places.</summary>
    public bool SwapsAxes => (QuarterTurns & 1) == 1;

    public PictureOrientation RotateClockwise() => this with { QuarterTurns = (QuarterTurns + 1) & 3 };

    public PictureOrientation RotateCounterclockwise() => this with { QuarterTurns = (QuarterTurns + 3) & 3 };

    public PictureOrientation MirrorHorizontal() => this with { FlipHorizontal = !FlipHorizontal };

    public PictureOrientation MirrorVertical() => this with { FlipVertical = !FlipVertical };

    /// <summary>The pixels of a <paramref name="width"/> × <paramref name="height"/> picture in packed RGB, turned and mirrored.</summary>
    public byte[] Apply(byte[] rgb, int width, int height)
    {
        if (this == None)
        {
            return rgb;
        }

        int outWidth = SwapsAxes ? height : width;
        byte[] result = new byte[rgb.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sx = FlipHorizontal ? width - 1 - x : x;
                int sy = FlipVertical ? height - 1 - y : y;
                int dx;
                int dy;
                switch (QuarterTurns & 3)
                {
                    case 1:
                        dx = height - 1 - sy;
                        dy = sx;
                        break;
                    case 2:
                        dx = width - 1 - sx;
                        dy = height - 1 - sy;
                        break;
                    case 3:
                        dx = sy;
                        dy = width - 1 - sx;
                        break;
                    default:
                        dx = sx;
                        dy = sy;
                        break;
                }

                int from = (y * width + x) * 3;
                int to = (dy * outWidth + dx) * 3;
                result[to] = rgb[from];
                result[to + 1] = rgb[from + 1];
                result[to + 2] = rgb[from + 2];
            }
        }

        return result;
    }
}

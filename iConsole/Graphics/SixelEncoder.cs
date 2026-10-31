using System.Text;

namespace iConsole.Graphics;

/// <summary>
/// Turns packed RGB pixels into a Sixel image: a device control string a terminal draws as a bitmap. The picture is reduced to
/// at most 255 colours with a median cut over a 15-bit histogram, so the cost is one pass over the pixels plus a fixed amount of
/// palette work, whatever the picture looks like.
/// </summary>
public static class SixelEncoder
{
    private const int MaxColors = 255;
    private const int BinCount = 32768;

    /// <summary>The Sixel sequence for <paramref name="rgb"/> (<c>width × height × 3</c> bytes), complete from the opening ESC P to the closing ESC \.</summary>
    public static string Encode(ReadOnlySpan<byte> rgb, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int pixels = width * height;
        if (rgb.Length < pixels * 3)
        {
            throw new ArgumentException("The pixel data is smaller than the picture.", nameof(rgb));
        }

        int[] histogram = new int[BinCount];
        for (int i = 0, o = 0; i < pixels; i++, o += 3)
        {
            histogram[Bin(rgb[o], rgb[o + 1], rgb[o + 2])]++;
        }

        byte[] binToColor = new byte[BinCount];
        (byte R, byte G, byte B)[] palette = BuildPalette(histogram, binToColor);

        byte[] indices = new byte[pixels];
        for (int i = 0, o = 0; i < pixels; i++, o += 3)
        {
            indices[i] = binToColor[Bin(rgb[o], rgb[o + 1], rgb[o + 2])];
        }

        StringBuilder sb = new(pixels / 3 + 2048);
        // P2 = 1 leaves a pixel this string does not set as it was; every pixel is set, so it only keeps the terminal from
        // clearing the cell background first. The raster attributes tell the terminal the size up front.
        sb.Append("\u001bP0;1;0q\"1;1;").Append(width).Append(';').Append(height);
        for (int i = 0; i < palette.Length; i++)
        {
            sb.Append('#').Append(i).Append(";2;")
                .Append(Percent(palette[i].R)).Append(';')
                .Append(Percent(palette[i].G)).Append(';')
                .Append(Percent(palette[i].B));
        }

        EncodeBands(sb, indices, width, height, palette.Length);
        sb.Append("\u001b\\");
        return sb.ToString();
    }

    private static void EncodeBands(StringBuilder sb, byte[] indices, int width, int height, int colors)
    {
        byte[] masks = new byte[colors * width];
        bool[] used = new bool[colors];
        List<int> usedList = new(colors);
        int bands = (height + 5) / 6;

        for (int band = 0; band < bands; band++)
        {
            int y0 = band * 6;
            int rows = Math.Min(6, height - y0);
            for (int r = 0; r < rows; r++)
            {
                int rowStart = (y0 + r) * width;
                byte bit = (byte)(1 << r);
                for (int x = 0; x < width; x++)
                {
                    int c = indices[rowStart + x];
                    if (!used[c])
                    {
                        used[c] = true;
                        usedList.Add(c);
                    }

                    masks[c * width + x] |= bit;
                }
            }

            bool first = true;
            foreach (int c in usedList)
            {
                if (!first)
                {
                    sb.Append('$');
                }

                first = false;
                sb.Append('#').Append(c);
                AppendRun(sb, masks.AsSpan(c * width, width));
            }

            foreach (int c in usedList)
            {
                used[c] = false;
                Array.Clear(masks, c * width, width);
            }

            usedList.Clear();
            if (band < bands - 1)
            {
                sb.Append('-');
            }
        }
    }

    /// <summary>One colour's row of six-pixel columns, run-length encoded; trailing empty columns are left out.</summary>
    private static void AppendRun(StringBuilder sb, ReadOnlySpan<byte> row)
    {
        int end = row.Length;
        while (end > 0 && row[end - 1] == 0)
        {
            end--;
        }

        int x = 0;
        while (x < end)
        {
            byte value = row[x];
            int run = 1;
            while (x + run < end && row[x + run] == value)
            {
                run++;
            }

            char ch = (char)(63 + value);
            if (run > 3)
            {
                sb.Append('!').Append(run).Append(ch);
            }
            else
            {
                sb.Append(ch, run);
            }

            x += run;
        }
    }

    private static int Bin(byte r, byte g, byte b) => ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);

    private static int Percent(byte value) => (value * 100 + 127) / 255;

    private struct Box
    {
        public int Start;
        public int End;
        public long Population;
    }

    /// <summary>Median cut: the used histogram bins are split into boxes of equal population, each box becoming one palette colour.</summary>
    private static (byte R, byte G, byte B)[] BuildPalette(int[] histogram, byte[] binToColor)
    {
        List<int> used = [];
        for (int bin = 0; bin < BinCount; bin++)
        {
            if (histogram[bin] > 0)
            {
                used.Add(bin);
            }
        }

        int[] bins = [.. used];
        List<Box> boxes = [new Box { Start = 0, End = bins.Length, Population = Population(histogram, bins, 0, bins.Length) }];

        while (boxes.Count < MaxColors)
        {
            int pick = -1;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].End - boxes[i].Start >= 2 && (pick < 0 || boxes[i].Population > boxes[pick].Population))
                {
                    pick = i;
                }
            }

            if (pick < 0)
            {
                break;
            }

            Box box = boxes[pick];
            int channel = WidestChannel(bins, box.Start, box.End);
            Array.Sort(bins, box.Start, box.End - box.Start, Comparer<int>.Create((a, b) =>
            {
                int diff = Channel(a, channel) - Channel(b, channel);
                return diff != 0 ? diff : a - b;
            }));

            long half = box.Population / 2;
            long running = 0;
            int split = box.Start + 1;
            for (int i = box.Start; i < box.End - 1; i++)
            {
                running += histogram[bins[i]];
                split = i + 1;
                if (running >= half)
                {
                    break;
                }
            }

            boxes[pick] = new Box { Start = box.Start, End = split, Population = Population(histogram, bins, box.Start, split) };
            boxes.Add(new Box { Start = split, End = box.End, Population = box.Population - boxes[pick].Population });
        }

        (byte R, byte G, byte B)[] palette = new (byte, byte, byte)[boxes.Count];
        for (int i = 0; i < boxes.Count; i++)
        {
            long r = 0, g = 0, b = 0, total = 0;
            for (int k = boxes[i].Start; k < boxes[i].End; k++)
            {
                int bin = bins[k];
                int count = histogram[bin];
                r += (long)Expand(Channel(bin, 0)) * count;
                g += (long)Expand(Channel(bin, 1)) * count;
                b += (long)Expand(Channel(bin, 2)) * count;
                total += count;
                binToColor[bin] = (byte)i;
            }

            total = Math.Max(1, total);
            palette[i] = ((byte)(r / total), (byte)(g / total), (byte)(b / total));
        }

        return palette;
    }

    private static long Population(int[] histogram, int[] bins, int start, int end)
    {
        long total = 0;
        for (int i = start; i < end; i++)
        {
            total += histogram[bins[i]];
        }

        return total;
    }

    /// <summary>0 = red, 1 = green, 2 = blue.</summary>
    private static int Channel(int bin, int channel) => channel switch
    {
        0 => bin >> 10,
        1 => (bin >> 5) & 31,
        _ => bin & 31,
    };

    private static int Expand(int fiveBits) => (fiveBits << 3) | (fiveBits >> 2);

    private static int WidestChannel(int[] bins, int start, int end)
    {
        Span<int> min = [31, 31, 31];
        Span<int> max = [0, 0, 0];
        for (int i = start; i < end; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                int v = Channel(bins[i], c);
                if (v < min[c]) { min[c] = v; }
                if (v > max[c]) { max[c] = v; }
            }
        }

        int best = 0;
        for (int c = 1; c < 3; c++)
        {
            if (max[c] - min[c] > max[best] - min[best])
            {
                best = c;
            }
        }

        return best;
    }
}

using System.Buffers.Binary;
using SkiaSharp;

namespace iConsole.Photoshop;

/// <summary>Whether the merged picture of a Photoshop file could be drawn, and if not, why.</summary>
public enum PsdPictureStatus
{
    /// <summary>The picture is in <see cref="PsdDocument.Picture"/>.</summary>
    Drawn,

    /// <summary>The file has no picture data to draw, or its colour mode is not one this reader draws (CMYK, Lab, indexed and the rest).</summary>
    NotDrawn,

    /// <summary>The picture is ZIP compressed, which this reader does not decompress.</summary>
    Compressed,

    /// <summary>The picture is larger than <see cref="PsdDocument.MaxPixels"/>.</summary>
    TooLarge,

    /// <summary>The picture data ends before the picture does.</summary>
    Truncated,
}

/// <summary>
/// A Photoshop file (PSD, and PSB for very large pictures) read for its header and its merged picture. The layers are only
/// counted, not read. The merged picture is drawn for 8- and 16-bit RGB and grayscale files whose data is raw or RLE
/// compressed; the viewer draws it as text art. Nothing in the file is run or loaded as a document.
/// </summary>
/// <param name="Version">1 for PSD, 2 for PSB.</param>
/// <param name="Width">The picture width in pixels.</param>
/// <param name="Height">The picture height in pixels.</param>
/// <param name="Channels">The channels in the file, including any alpha or spot channels.</param>
/// <param name="Depth">Bits per channel.</param>
/// <param name="ColorMode">The colour mode's name, for example <c>RGB</c>.</param>
/// <param name="LayerCount">The layers in the layer section, or null when the file has none.</param>
/// <param name="Compression">The merged picture's compression, for example <c>RLE</c>.</param>
/// <param name="PictureStatus">Whether <paramref name="Picture"/> was drawn, and if not, why.</param>
/// <param name="Picture">The merged picture, opaque, or null when it was not drawn. The caller disposes it.</param>
public sealed record PsdDocument(
    int Version,
    int Width,
    int Height,
    int Channels,
    int Depth,
    string ColorMode,
    int? LayerCount,
    string Compression,
    PsdPictureStatus PictureStatus,
    SKBitmap? Picture)
{
    /// <summary>The most pixels drawn; a larger picture is reported as <see cref="PsdPictureStatus.TooLarge"/>.</summary>
    public const long MaxPixels = 16_000_000;

    private const int MaxDimension = 300_000;

    /// <summary>Reads the header and the merged picture. Throws <see cref="FormatException"/> when the bytes are not a Photoshop file or the header is out of range.</summary>
    public static PsdDocument Parse(byte[] bytes)
    {
        if (bytes.Length < 26 || bytes[0] != '8' || bytes[1] != 'B' || bytes[2] != 'P' || bytes[3] != 'S')
        {
            throw new FormatException("the file does not start with a Photoshop header");
        }

        int version = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        if (version is not (1 or 2))
        {
            throw new FormatException("the Photoshop version is not known");
        }

        // A PSB file has 8-byte lengths for its large sections; a PSD file has 4-byte ones.
        int lengthSize = version == 2 ? 8 : 4;
        int channels = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(12));
        long height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(14));
        long width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(18));
        int depth = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(22));
        int mode = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(24));
        if (channels is < 1 or > 56 || width is < 1 or > MaxDimension || height is < 1 or > MaxDimension)
        {
            throw new FormatException("the picture size or channel count is out of range");
        }

        int position = 26;
        position = SkipSection(bytes, position, 4); // colour mode data
        position = SkipSection(bytes, position, 4); // image resources

        // The layer and mask section: the layer count leads its layer info, and the picture data follows the whole section.
        int? layerCount = null;
        long maskLength = ReadLength(bytes, position, lengthSize);
        int maskStart = position + lengthSize;
        if (maskLength > 0 && maskStart + lengthSize <= bytes.Length)
        {
            long infoLength = ReadLength(bytes, maskStart, lengthSize);
            int infoStart = maskStart + lengthSize;
            if (infoLength >= 2 && infoStart + 2 <= bytes.Length)
            {
                layerCount = Math.Abs(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(infoStart)));
            }
        }

        position = (int)Math.Min(bytes.Length, maskStart + maskLength);
        if (position + 2 > bytes.Length)
        {
            throw new FormatException("the file ends before its picture data");
        }

        int compressionCode = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position));
        string compression = compressionCode switch
        {
            0 => "Raw",
            1 => "RLE",
            2 => "ZIP",
            3 => "ZIP with prediction",
            _ => "unknown",
        };

        int pixels = (int)width;
        int rows = (int)height;
        long total = width * height;
        (PsdPictureStatus status, SKBitmap? picture) = Draw(bytes, position + 2, compressionCode, version == 2, depth, mode, channels, pixels, rows, total);

        return new PsdDocument(version, pixels, rows, channels, depth, ColorModeName(mode), layerCount, compression, status, picture);
    }

    /// <summary>Draws the merged picture when the file's colour mode, depth and compression allow it; otherwise says why not.</summary>
    private static (PsdPictureStatus, SKBitmap?) Draw(byte[] bytes, int dataStart, int compressionCode, bool large, int depth, int mode, int channels, int width, int height, long total)
    {
        bool grey = mode == 1;
        bool rgb = mode == 3;
        if (!(grey || rgb) || depth is not (8 or 16))
        {
            return (PsdPictureStatus.NotDrawn, null);
        }

        if (compressionCode is 2 or 3)
        {
            return (PsdPictureStatus.Compressed, null);
        }

        if (compressionCode is not (0 or 1))
        {
            return (PsdPictureStatus.NotDrawn, null);
        }

        int needed = grey ? 1 : 3;
        if (channels < needed)
        {
            return (PsdPictureStatus.NotDrawn, null);
        }

        if (total > MaxPixels)
        {
            return (PsdPictureStatus.TooLarge, null);
        }

        int bytesPerSample = depth / 8;
        var planes = new byte[needed][];
        bool complete = compressionCode == 0
            ? ReadRaw(bytes, dataStart, planes, width, height, bytesPerSample)
            : ReadRle(bytes, dataStart, planes, channels, width, height, bytesPerSample, large);
        if (!complete)
        {
            return (PsdPictureStatus.Truncated, null);
        }

        var colors = new SKColor[(long)width * height];
        for (int i = 0; i < colors.Length; i++)
        {
            byte r = planes[0][i];
            byte g = grey ? r : planes[1][i];
            byte b = grey ? r : planes[2][i];
            colors[i] = new SKColor(r, g, b);
        }

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        bitmap.Pixels = colors;
        return (PsdPictureStatus.Drawn, bitmap);
    }

    /// <summary>Raw data: every channel is one plane after another. Only the planes drawn are kept, at 8 bits each.</summary>
    private static bool ReadRaw(byte[] bytes, int dataStart, byte[][] planes, int width, int height, int bytesPerSample)
    {
        long planeBytes = (long)width * height * bytesPerSample;
        for (int c = 0; c < planes.Length; c++)
        {
            long start = dataStart + c * planeBytes;
            if (start + planeBytes > bytes.Length)
            {
                return false;
            }

            planes[c] = new byte[(long)width * height];
            for (int i = 0; i < planes[c].Length; i++)
            {
                // A 16-bit sample is big-endian, so its high byte comes first.
                planes[c][i] = bytes[start + i * bytesPerSample];
            }
        }

        return true;
    }

    /// <summary>
    /// RLE data: a byte count per row of every channel, then each row's PackBits data. A PSD count is two bytes; a PSB count is four.
    /// </summary>
    private static bool ReadRle(byte[] bytes, int dataStart, byte[][] planes, int channels, int width, int height, int bytesPerSample, bool large)
    {
        int countSize = large ? 4 : 2;
        long entries = (long)channels * height;
        long position = dataStart + entries * countSize;
        if (position > bytes.Length)
        {
            return false;
        }

        var row = new byte[width * bytesPerSample];
        for (int c = 0; c < planes.Length; c++)
        {
            planes[c] = new byte[(long)width * height];
            for (int y = 0; y < height; y++)
            {
                long entry = dataStart + ((long)c * height + y) * countSize;
                long count = large
                    ? BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan((int)entry))
                    : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan((int)entry));
                if (position + count > bytes.Length || !UnpackBits(bytes.AsSpan((int)position, (int)count), row))
                {
                    return false;
                }

                position += count;
                for (int x = 0; x < width; x++)
                {
                    planes[c][y * width + x] = row[x * bytesPerSample];
                }
            }
        }

        return true;
    }

    /// <summary>PackBits: a byte from 0 to 127 starts a literal run of one more byte; 129 to 255 repeats the next byte 257 minus it times; 128 does nothing.</summary>
    private static bool UnpackBits(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        int input = 0, output = 0;
        while (output < destination.Length)
        {
            if (input >= source.Length)
            {
                return false;
            }

            int control = source[input++];
            if (control < 128)
            {
                int run = control + 1;
                if (input + run > source.Length || output + run > destination.Length)
                {
                    return false;
                }

                source.Slice(input, run).CopyTo(destination[output..]);
                input += run;
                output += run;
            }
            else if (control > 128)
            {
                int run = 257 - control;
                if (input >= source.Length || output + run > destination.Length)
                {
                    return false;
                }

                destination.Slice(output, run).Fill(source[input++]);
                output += run;
            }
        }

        return true;
    }

    /// <summary>Skips a section that begins with its length; returns the position after it.</summary>
    private static int SkipSection(byte[] bytes, int position, int lengthSize)
    {
        long length = ReadLength(bytes, position, lengthSize);
        long next = position + lengthSize + length;
        if (next > bytes.Length)
        {
            throw new FormatException("a section runs past the end of the file");
        }

        return (int)next;
    }

    private static long ReadLength(byte[] bytes, int position, int lengthSize)
    {
        if (position + lengthSize > bytes.Length)
        {
            return 0;
        }

        return lengthSize == 8
            ? (long)BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(position))
            : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position));
    }

    private static string ColorModeName(int mode) => mode switch
    {
        0 => "Bitmap",
        1 => "Grayscale",
        2 => "Indexed",
        3 => "RGB",
        4 => "CMYK",
        7 => "Multichannel",
        8 => "Duotone",
        9 => "Lab",
        _ => "unknown",
    };
}

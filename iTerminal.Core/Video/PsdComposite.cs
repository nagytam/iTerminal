using SkiaSharp;

namespace iTerminal.Core.Video;

/// <summary>What the header of a Photoshop file says, and where its composite picture starts.</summary>
internal sealed record PsdHeader(int Version, int Channels, int Height, int Width, int Depth, int ColorMode, byte[]? Palette, long ImageDataOffset);

/// <summary>
/// The composite picture of a Photoshop file, read straight from the file. The layout follows the Adobe specification:
/// a fixed header, then the colour mode data, the image resources and the layer and mask block, each skipped by its
/// length, and then the composite image: a compression code and the channels, planar and row by row.
/// </summary>
internal static class PsdComposite
{
    private const int MaxPixels = 30_000_000;

    internal static PsdHeader ReadHeader(Stream stream)
    {
        Span<byte> header = stackalloc byte[26];
        ReadExactly(stream, header);
        if (header[..4] is not [(byte)'8', (byte)'B', (byte)'P', (byte)'S'])
        {
            throw new InvalidDataException("Not a Photoshop file.");
        }

        int version = BigEndian16(header[4..]);
        int channels = BigEndian16(header[12..]);
        int height = (int)BigEndian32(header[14..]);
        int width = (int)BigEndian32(header[18..]);
        int depth = BigEndian16(header[22..]);
        int colorMode = BigEndian16(header[24..]);

        if (version is not (1 or 2)
            || depth is not (8 or 16)
            || colorMode is not (1 or 2 or 3 or 4)
            || channels < 1 || channels > 56
            || width <= 0 || height <= 0
            || (long)width * height > MaxPixels
            || (colorMode == 3 && channels < 3)
            || (colorMode == 4 && channels < 4))
        {
            throw new InvalidDataException("Unsupported Photoshop layout.");
        }

        uint colorModeLength = ReadUInt32(stream);
        byte[]? palette = null;
        if (colorMode == 2 && colorModeLength >= 768)
        {
            palette = new byte[768];
            ReadExactly(stream, palette);
            stream.Position += colorModeLength - 768;
        }
        else
        {
            stream.Position += colorModeLength;
        }

        // Each length is read before the position moves: `stream.Position += Read(...)` would read the position first and lose the read.
        long resourcesLength = ReadUInt32(stream);
        stream.Position += resourcesLength;
        long layerLength = version == 2 ? ReadInt64(stream) : ReadUInt32(stream);
        stream.Position += layerLength;

        return new PsdHeader(version, channels, height, width, depth, colorMode, palette, stream.Position);
    }

    /// <summary>Reads the composite picture of the file at <paramref name="path"/> into an RGBA bitmap.</summary>
    internal static SKBitmap DecodeFile(string path, PsdHeader header)
    {
        using FileStream stream = File.OpenRead(path);
        return Decode(stream, header);
    }

    internal static SKBitmap Decode(Stream stream, PsdHeader header)
    {
        stream.Position = header.ImageDataOffset;
        int compression = BigEndian16(ReadBytes(stream, 2));
        int bytesPerSample = header.Depth / 8;
        int wanted = header.ColorMode switch
        {
            3 => 3,
            4 => 4,
            _ => 1,
        };

        byte[][] planes = new byte[wanted][];
        long planeBytes = (long)header.Width * header.Height * bytesPerSample;
        switch (compression)
        {
            case 0:
                for (int channel = 0; channel < wanted; channel++)
                {
                    stream.Position = header.ImageDataOffset + 2 + planeBytes * channel;
                    planes[channel] = ReadBytes(stream, (int)planeBytes);
                }

                break;
            case 1:
                ReadRunLength(stream, header, planes, bytesPerSample);
                break;
            default:
                throw new NotSupportedException("This Photoshop file uses a compression the viewer does not read.");
        }

        return ToBitmap(header, planes, bytesPerSample);
    }

    private static void ReadRunLength(Stream stream, PsdHeader header, byte[][] planes, int bytesPerSample)
    {
        int rowsPerChannel = header.Height;
        int rowCount = rowsPerChannel * header.Channels;
        long[] lengths = new long[rowCount];
        for (int i = 0; i < rowCount; i++)
        {
            lengths[i] = header.Version == 2 ? ReadUInt32(stream) : BigEndian16(ReadBytes(stream, 2));
        }

        long dataStart = stream.Position;
        int rowBytes = header.Width * bytesPerSample;
        for (int channel = 0; channel < planes.Length; channel++)
        {
            long position = dataStart;
            for (int i = 0; i < channel * rowsPerChannel; i++)
            {
                position += lengths[i];
            }

            planes[channel] = new byte[rowBytes * header.Height];
            for (int row = 0; row < rowsPerChannel; row++)
            {
                stream.Position = position;
                byte[] packed = ReadBytes(stream, (int)lengths[channel * rowsPerChannel + row]);
                position += packed.Length;
                Unpack(packed, planes[channel].AsSpan(row * rowBytes, rowBytes));
            }
        }
    }

    /// <summary>PackBits: a control byte 0..127 copies that many plus one literal bytes; -127..-1 repeats the next byte 1 - control times.</summary>
    internal static void Unpack(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        int read = 0;
        int written = 0;
        while (read < source.Length && written < destination.Length)
        {
            sbyte control = (sbyte)source[read++];
            if (control >= 0)
            {
                int literal = Math.Min(control + 1, Math.Min(source.Length - read, destination.Length - written));
                source.Slice(read, literal).CopyTo(destination[written..]);
                read += literal;
                written += literal;
            }
            else if (control != -128 && read < source.Length)
            {
                int run = Math.Min(1 - control, destination.Length - written);
                destination.Slice(written, run).Fill(source[read++]);
                written += run;
            }
        }
    }

    private static SKBitmap ToBitmap(PsdHeader header, byte[][] planes, int bytesPerSample)
    {
        int count = header.Width * header.Height;
        var colors = new SKColor[count];
        for (int i = 0; i < count; i++)
        {
            // A 16-bit sample is big-endian, so its first byte is the high one, which is the one kept.
            int at = i * bytesPerSample;
            colors[i] = header.ColorMode switch
            {
                3 => new SKColor(planes[0][at], planes[1][at], planes[2][at]),
                4 => Cmyk(planes[0][at], planes[1][at], planes[2][at], planes[3][at]),
                2 when header.Palette is not null => Indexed(header.Palette, planes[0][at]),
                _ => new SKColor(planes[0][at], planes[0][at], planes[0][at]),
            };
        }

        var bitmap = new SKBitmap(header.Width, header.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        bitmap.Pixels = colors;
        return bitmap;
    }

    /// <summary>Photoshop stores CMYK with 255 meaning no ink, so each colour is its channel multiplied by black.</summary>
    private static SKColor Cmyk(byte c, byte m, byte y, byte k) =>
        new((byte)(c * k / 255), (byte)(m * k / 255), (byte)(y * k / 255));

    private static SKColor Indexed(byte[] palette, byte index) =>
        new(palette[index], palette[256 + index], palette[512 + index]);

    private static int BigEndian16(ReadOnlySpan<byte> bytes) => (bytes[0] << 8) | bytes[1];

    private static uint BigEndian32(ReadOnlySpan<byte> bytes) =>
        ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

    private static uint ReadUInt32(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[4];
        ReadExactly(stream, bytes);
        return BigEndian32(bytes);
    }

    private static long ReadInt64(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[8];
        ReadExactly(stream, bytes);
        long value = 0;
        foreach (byte b in bytes)
        {
            value = (value << 8) | b;
        }

        return value;
    }

    private static byte[] ReadBytes(Stream stream, int count)
    {
        byte[] bytes = new byte[count];
        ReadExactly(stream, bytes);
        return bytes;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        while (buffer.Length > 0)
        {
            int read = stream.Read(buffer);
            if (read == 0)
            {
                throw new InvalidDataException("The Photoshop file ends early.");
            }

            buffer = buffer[read..];
        }
    }
}

using System.Buffers.Binary;
using SkiaSharp;

namespace iTerminal.Core.Services.Scanning;

/// <summary>
/// Turns scanner output into pictures the rest of the app can use. TWAIN hands over a device-independent
/// bitmap without its file header; <see cref="WrapDibAsBmp"/> adds it so SkiaSharp can decode it. Saving and
/// previewing both go through SkiaSharp, which reads BMP and PNG and writes PNG and JPEG.
/// </summary>
public static class ScanImageCodec
{
    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;
    private const uint BiBitfields = 3;
    private const uint BiAlphaBitfields = 6;

    /// <summary>The ramp from the lightest to the densest character of the ASCII preview.</summary>
    internal const string AsciiRamp = " .:-=+*#%@";

    /// <summary>
    /// Adds the 14-byte BITMAPFILEHEADER in front of a DIB (BITMAPINFOHEADER, optional bit masks, palette, pixels).
    /// Throws <see cref="InvalidDataException"/> when the header cannot be a DIB.
    /// </summary>
    public static byte[] WrapDibAsBmp(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < InfoHeaderSize)
        {
            throw new InvalidDataException("The scanner returned less than a bitmap header.");
        }

        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(dib);
        ushort bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
        uint colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);
        if (headerSize < InfoHeaderSize || headerSize > dib.Length)
        {
            throw new InvalidDataException("The scanner returned a bitmap with a bad header size.");
        }

        // With a 40-byte header the bit masks follow it; larger headers carry them inside.
        int masks = headerSize == InfoHeaderSize && compression == BiBitfields ? 12
            : headerSize == InfoHeaderSize && compression == BiAlphaBitfields ? 16
            : 0;

        // A palette is present for 8 bits and fewer (always), and beyond that only when it is named.
        long paletteEntries = bitCount <= 8
            ? (colorsUsed != 0 ? colorsUsed : 1u << bitCount)
            : colorsUsed;
        long paletteBytes = paletteEntries * 4;

        long pixelOffset = FileHeaderSize + headerSize + masks + paletteBytes;
        long fileSize = FileHeaderSize + (long)dib.Length;
        if (pixelOffset > uint.MaxValue || fileSize > uint.MaxValue)
        {
            throw new InvalidDataException("The scanned picture is too large for a BMP file.");
        }

        byte[] bmp = new byte[fileSize];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2), (uint)fileSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10), (uint)pixelOffset);
        dib.CopyTo(bmp.AsSpan(FileHeaderSize));
        return bmp;
    }

    /// <summary>Decodes a scanned page, or returns null when SkiaSharp cannot read it.</summary>
    public static SKBitmap? Decode(ScanPage page) => SKBitmap.Decode(page.Encoded);

    /// <summary>Encodes a scanned page as PNG or JPEG. Throws <see cref="InvalidDataException"/> when the page cannot be decoded.</summary>
    public static byte[] Encode(ScanPage page, ScanFileFormat format)
    {
        using SKBitmap bitmap = Decode(page) ?? throw new InvalidDataException("The scanned picture could not be read.");
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = format == ScanFileFormat.Jpeg
            ? image.Encode(SKEncodedImageFormat.Jpeg, 92)
            : image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// The picture as characters: one sample per cell, darker pixels as denser characters. It is a preview of
    /// what the page holds, not a faithful copy: thin lines can drop out between samples.
    /// </summary>
    public static IReadOnlyList<string> AsciiPreview(SKBitmap bitmap, int columns, int rows)
    {
        if (columns <= 0 || rows <= 0 || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return [];
        }

        var lines = new List<string>(rows);
        for (int row = 0; row < rows; row++)
        {
            var line = new char[columns];
            int y = Math.Min(bitmap.Height - 1, (int)((row + 0.5) * bitmap.Height / rows));
            for (int column = 0; column < columns; column++)
            {
                int x = Math.Min(bitmap.Width - 1, (int)((column + 0.5) * bitmap.Width / columns));
                SKColor pixel = bitmap.GetPixel(x, y);
                double luminance = (0.299 * pixel.Red + 0.587 * pixel.Green + 0.114 * pixel.Blue) / 255.0;
                int index = (int)Math.Round((1.0 - luminance) * (AsciiRamp.Length - 1));
                line[column] = AsciiRamp[Math.Clamp(index, 0, AsciiRamp.Length - 1)];
            }

            lines.Add(new string(line));
        }

        return lines;
    }
}

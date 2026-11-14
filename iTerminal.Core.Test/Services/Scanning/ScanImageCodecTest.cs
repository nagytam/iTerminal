using System.Buffers.Binary;
using iTerminal.Core.Services.Scanning;
using SkiaSharp;

namespace iTerminal.Core.Test.Services.Scanning;

[TestClass]
public class ScanImageCodecTest
{
    /// <summary>A 40-byte BITMAPINFOHEADER with no palette and no compression, followed by the pixel rows.</summary>
    private static byte[] Dib(int width, int height, ushort bitCount, uint colorsUsed, byte[] pixels)
    {
        var dib = new byte[40 + pixels.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(32), colorsUsed);
        pixels.CopyTo(dib, 40);
        return dib;
    }

    /// <summary>A 2 by 1, 24-bit picture: a red pixel and a black pixel, each row padded to four bytes.</summary>
    private static byte[] TwoPixelDib() => Dib(2, 1, 24, 0, [0, 0, 255, 0, 0, 0, 0, 0]);

    [TestMethod]
    public void WrapDibAsBmp_AddsTheFileHeader_AndTheDecoderReadsThePicture()
    {
        byte[] dib = TwoPixelDib();

        byte[] bmp = ScanImageCodec.WrapDibAsBmp(dib);

        Assert.AreEqual((byte)'B', bmp[0]);
        Assert.AreEqual((byte)'M', bmp[1]);
        Assert.AreEqual(14 + dib.Length, (int)BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(2)));
        Assert.AreEqual(14 + 40, (int)BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(10)), "no palette: the pixels follow the header");

        using SKBitmap? decoded = SKBitmap.Decode(bmp);
        Assert.IsNotNull(decoded);
        Assert.AreEqual(2, decoded.Width);
        Assert.AreEqual(1, decoded.Height);
    }

    [TestMethod]
    public void WrapDibAsBmp_PlacesAnEightBitPalette_BeforeThePixels()
    {
        // 8 bits and no colours named: the palette has 256 entries of four bytes each.
        byte[] dib = Dib(1, 1, 8, 0, new byte[1024 + 4]);

        byte[] bmp = ScanImageCodec.WrapDibAsBmp(dib);

        Assert.AreEqual(14 + 40 + 256 * 4, (int)BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(10)));
    }

    [TestMethod]
    public void WrapDibAsBmp_RejectsWhatIsNotABitmapHeader()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => ScanImageCodec.WrapDibAsBmp(new byte[10]));
    }

    [TestMethod]
    public void AsciiPreview_DrawsDarkPixelsDenseAndLightOnesBlank()
    {
        using var bitmap = new SKBitmap(2, 1);
        bitmap.SetPixel(0, 0, SKColors.Black);
        bitmap.SetPixel(1, 0, SKColors.White);

        IReadOnlyList<string> lines = ScanImageCodec.AsciiPreview(bitmap, 2, 1);

        Assert.HasCount(1, lines);
        Assert.AreEqual("@ ", lines[0]);
    }

    [TestMethod]
    public void AsciiPreview_OfNothingIsEmpty()
    {
        using var bitmap = new SKBitmap(2, 2);
        Assert.IsEmpty(ScanImageCodec.AsciiPreview(bitmap, 0, 5));
    }

    [TestMethod]
    public void Encode_WritesPngAndJpegSignatures()
    {
        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(SKColors.White);
        var page = new ScanPage(ScanImageCodec.WrapDibAsBmp(Dib(4, 4, 24, 0, new byte[4 * 12])));

        byte[] png = ScanImageCodec.Encode(page, ScanFileFormat.Png);
        byte[] jpeg = ScanImageCodec.Encode(page, ScanFileFormat.Jpeg);

        CollectionAssert.AreEqual(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, png[..4]);
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0xD8 }, jpeg[..2]);
    }
}

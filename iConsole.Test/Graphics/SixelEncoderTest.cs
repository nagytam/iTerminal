using iConsole.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Graphics;

[TestClass]
public class SixelEncoderTest
{
    private static byte[] Solid(int width, int height, byte r, byte g, byte b)
    {
        byte[] rgb = new byte[width * height * 3];
        for (int i = 0; i < rgb.Length; i += 3)
        {
            rgb[i] = r;
            rgb[i + 1] = g;
            rgb[i + 2] = b;
        }

        return rgb;
    }

    [TestMethod]
    public void Encode_IsADeviceControlStringWithTheRasterSize()
    {
        string sixel = SixelEncoder.Encode(Solid(10, 12, 255, 0, 0), 10, 12);

        Assert.IsTrue(sixel.StartsWith("\u001bP0;1;0q\"1;1;10;12", StringComparison.Ordinal));
        Assert.IsTrue(sixel.EndsWith("\u001b\\", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Encode_DefinesAPaletteColourInPercent()
    {
        string sixel = SixelEncoder.Encode(Solid(4, 6, 255, 0, 0), 4, 6);

        StringAssert.Contains(sixel, "#0;2;100;0;0");
    }

    [TestMethod]
    public void Encode_OneSolidBand_IsOneRunOfFullColumns()
    {
        // Six rows set in every column is bit mask 63, which is the character '~' (63 + 63 = 126); a run longer than three is counted.
        string sixel = SixelEncoder.Encode(Solid(8, 6, 0, 255, 0), 8, 6);

        StringAssert.Contains(sixel, "#0!8~");
        Assert.IsFalse(sixel.Contains('-'), "a single band has no line feed");
    }

    [TestMethod]
    public void Encode_StartsANewBandEverySixRows()
    {
        string sixel = SixelEncoder.Encode(Solid(4, 13, 10, 20, 30), 4, 13);

        // 13 rows are 3 bands, so two line feeds.
        Assert.AreEqual(2, sixel.Count(c => c == '-'));
    }

    [TestMethod]
    public void Encode_ABandWithTwoColours_SeparatesThemWithACarriageReturn()
    {
        byte[] rgb = new byte[4 * 6 * 3];
        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int o = (y * 4 + x) * 3;
                if (x < 2)
                {
                    rgb[o] = 255;
                }
                else
                {
                    rgb[o + 2] = 255;
                }
            }
        }

        string sixel = SixelEncoder.Encode(rgb, 4, 6);

        Assert.AreEqual(1, sixel.Count(c => c == '$'));
        StringAssert.Contains(sixel, "~~");
    }

    [TestMethod]
    public void Encode_ManyColours_StaysWithinThePaletteLimit()
    {
        const int width = 64;
        const int height = 64;
        byte[] rgb = new byte[width * height * 3];
        for (int i = 0; i < width * height; i++)
        {
            rgb[i * 3] = (byte)(i * 7);
            rgb[i * 3 + 1] = (byte)(i * 13 / 5);
            rgb[i * 3 + 2] = (byte)(i * 3);
        }

        string sixel = SixelEncoder.Encode(rgb, width, height);

        int definitions = sixel.Split('#').Count(part => part.Contains(";2;"));
        Assert.IsTrue(definitions is > 1 and <= 255, $"palette of {definitions} colours");
    }

    [TestMethod]
    public void Encode_RejectsPixelDataThatIsTooSmall()
    {
        Assert.ThrowsExactly<ArgumentException>(() => SixelEncoder.Encode(new byte[5], 4, 4));
    }
}

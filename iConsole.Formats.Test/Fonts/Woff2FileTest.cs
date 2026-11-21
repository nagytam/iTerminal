using System.Buffers.Binary;
using iConsole.Fonts;
using iConsole.Test.Common;
using iConsole.Test.Services;

namespace iConsole.Test.Fonts;

[TestClass]
public class Woff2FileTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ToSfnt_UnwrapsTheSampleToTheSameFontAsItsTrueTypeOriginal()
    {
        FontInfo original = FontFileReader.Read(SampleFiles.Bytes("example.ttf"));
        FontInfo wrapped = FontFileReader.Read(Woff2File.ToSfnt(SampleFiles.Bytes("example.woff2")));

        Assert.AreEqual(original.FamilyName, wrapped.FamilyName);
        Assert.AreEqual(original.SubfamilyName, wrapped.SubfamilyName);
        Assert.AreEqual(original.FullName, wrapped.FullName);
        Assert.AreEqual(original.PostScriptName, wrapped.PostScriptName);
        Assert.AreEqual(original.UnitsPerEm, wrapped.UnitsPerEm);
        Assert.AreEqual(original.GlyphCount, wrapped.GlyphCount);
        Assert.AreEqual(original.CharacterCount, wrapped.CharacterCount);
        Assert.AreEqual("TrueType", wrapped.Outlines);
    }

    /// <summary>The sample's hmtx table is transformed in the WOFF 2.0 file, so this checks the rebuilt table byte for byte.</summary>
    [Timeout(60_000)]
    [TestMethod]
    public void ToSfnt_RebuildsTheTransformedMetricsToTheOriginalBytes()
    {
        byte[] original = SampleFiles.Bytes("example.ttf");
        byte[] unwrapped = Woff2File.ToSfnt(SampleFiles.Bytes("example.woff2"));

        CollectionAssert.AreEqual(TableData(original, "hmtx"), TableData(unwrapped, "hmtx"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToSfnt_SetsTheChecksumAdjustmentSoTheFontSumsToTheMagicValue()
    {
        byte[] unwrapped = Woff2File.ToSfnt(SampleFiles.Bytes("example.woff2"));

        uint sum = 0;
        for (int i = 0; i + 4 <= unwrapped.Length; i += 4)
        {
            sum += BinaryPrimitives.ReadUInt32BigEndian(unwrapped.AsSpan(i));
        }

        Assert.AreEqual(0xB1B0AFBAu, sum);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsWoff2_TellsTheWebFontFromTheOthers()
    {
        Assert.IsTrue(Woff2File.IsWoff2(SampleFiles.Bytes("example.woff2")));
        Assert.IsFalse(Woff2File.IsWoff2(SampleFiles.Bytes("example.woff")));
        Assert.IsFalse(Woff2File.IsWoff2(SampleFiles.Bytes("example.ttf")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Unwrap_DecodesAWoff2FileAndLeavesAnInstalledFontAsItIs()
    {
        byte[] woff2 = SampleFiles.Bytes("example.woff2");
        byte[] ttf = SampleFiles.Bytes("example.ttf");

        Assert.AreEqual(FontFileReader.Read(ttf).GlyphCount, FontFileReader.Read(WoffFile.Unwrap(woff2)).GlyphCount);
        Assert.AreSame(ttf, WoffFile.Unwrap(ttf));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToSfnt_RefusesAFileCutShort()
    {
        byte[] woff2 = SampleFiles.Bytes("example.woff2");

        Assert.ThrowsExactly<FormatException>(() => Woff2File.ToSfnt(woff2[..40]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToSfnt_RefusesACorruptBrotliStream()
    {
        byte[] woff2 = SampleFiles.Bytes("example.woff2");
        uint compressed = BinaryPrimitives.ReadUInt32BigEndian(woff2.AsSpan(20));

        // With no metadata or private block, the compressed stream is the end of the file.
        Array.Fill(woff2, (byte)0xFF, woff2.Length - (int)compressed, (int)compressed);

        Assert.ThrowsExactly<FormatException>(() => Woff2File.ToSfnt(woff2));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToSfnt_RefusesAFontCollection()
    {
        byte[] woff2 = SampleFiles.Bytes("example.woff2");
        "ttcf"u8.CopyTo(woff2.AsSpan(4));

        Assert.ThrowsExactly<FormatException>(() => Woff2File.ToSfnt(woff2));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToSfnt_RefusesAnInstalledFont()
    {
        Assert.ThrowsExactly<FormatException>(() => Woff2File.ToSfnt(SampleFiles.Bytes("example.ttf")));
    }

    /// <summary>The bytes of one table in an sfnt font, found through its directory.</summary>
    private static byte[] TableData(byte[] font, string tag)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int record = 12 + i * 16;
            if (System.Text.Encoding.ASCII.GetString(font, record, 4) == tag)
            {
                int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8));
                int length = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12));
                return font[offset..(offset + length)];
            }
        }

        Assert.Fail($"the font has no {tag} table");
        return [];
    }
}

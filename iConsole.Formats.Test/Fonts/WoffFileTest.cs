using iConsole.Formats.Fonts;
using iConsole.Test.Common;
using iConsole.Test.Services;

namespace iConsole.Formats.Test.Fonts;

[TestClass]
public class WoffFileTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ToSfnt_UnwrapsTheSampleToTheSameFontAsItsTrueTypeOriginal()
    {
        FontInfo original = FontFileReader.Read(SampleFiles.Bytes("example.ttf"));
        FontInfo wrapped = FontFileReader.Read(WoffFile.ToSfnt(SampleFiles.Bytes("example.woff")));

        Assert.AreEqual(original.FamilyName, wrapped.FamilyName);
        Assert.AreEqual(original.SubfamilyName, wrapped.SubfamilyName);
        Assert.AreEqual(original.FullName, wrapped.FullName);
        Assert.AreEqual(original.PostScriptName, wrapped.PostScriptName);
        Assert.AreEqual(original.UnitsPerEm, wrapped.UnitsPerEm);
        Assert.AreEqual(original.GlyphCount, wrapped.GlyphCount);
        Assert.AreEqual(original.CharacterCount, wrapped.CharacterCount);
        Assert.AreEqual("TrueType", wrapped.Outlines);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsWoff_TellsTheWebFontFromTheInstalledOne()
    {
        Assert.IsTrue(WoffFile.IsWoff(SampleFiles.Bytes("example.woff")));
        Assert.IsFalse(WoffFile.IsWoff(SampleFiles.Bytes("example.ttf")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Unwrap_LeavesAnInstalledFontAsItIs()
    {
        byte[] ttf = SampleFiles.Bytes("example.ttf");

        Assert.AreSame(ttf, WoffFile.Unwrap(ttf));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToSfnt_RefusesAFileCutShort()
    {
        byte[] woff = SampleFiles.Bytes("example.woff");

        Assert.ThrowsExactly<FormatException>(() => WoffFile.ToSfnt(woff[..60]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToSfnt_RefusesACorruptCompressedTable()
    {
        byte[] woff = SampleFiles.Bytes("example.woff");
        int count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(woff.AsSpan(12));
        int offset = -1, compressed = 0;
        for (int i = 0; i < count && offset < 0; i++)
        {
            int entry = 44 + i * 20;
            int comp = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(entry + 8));
            int orig = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(entry + 12));
            if (comp != orig)
            {
                offset = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(entry + 4));
                compressed = comp;
            }
        }

        if (offset < 0)
        {
            Assert.Inconclusive("no table of the sample is compressed");
        }

        // Zeros are not a zlib stream, so the table cannot be inflated.
        Array.Fill(woff, (byte)0xFF, offset, compressed);

        Assert.ThrowsExactly<FormatException>(() => WoffFile.ToSfnt(woff));
    }
}

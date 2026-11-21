using System.Buffers.Binary;
using System.Text;
using iConsole.Services;

namespace iConsole.Test.Services;

/// <summary>
/// Reading a Kindle MOBI file. The books are built here: a Palm database with a PalmDOC record 0, a MOBI header and
/// PalmDOC text records, so the tests do not depend on a real book being on the machine.
/// </summary>
[TestClass]
public class MobiTextReaderTest
{
    private const int MobiHeaderLength = 0x100;

    /// <summary>PalmDOC literal bytes: a run of up to eight bytes, each run started by its count.</summary>
    private static byte[] Literal(string text)
    {
        List<byte> bytes = [];
        byte[] raw = Encoding.UTF8.GetBytes(text);
        for (int i = 0; i < raw.Length; i += 8)
        {
            int count = Math.Min(8, raw.Length - i);
            bytes.Add((byte)count);
            bytes.AddRange(raw.Skip(i).Take(count));
        }

        return [.. bytes];
    }

    /// <summary>A PalmDB file with a MOBI header that declares the given compression, encoding and trailing-entry flags.</summary>
    private static byte[] MakeMobi(byte[][] textRecords, ushort compression = 2, ushort extraFlags = 0, uint encoding = 65001,
        (uint Index, uint Count)? huffman = null, params byte[][] trailingRecords)
    {
        int recordCount = textRecords.Length + trailingRecords.Length + 1;
        byte[] record0 = new byte[16 + MobiHeaderLength];
        BinaryPrimitives.WriteUInt16BigEndian(record0, compression);
        BinaryPrimitives.WriteUInt32BigEndian(record0.AsSpan(4), (uint)textRecords.Sum(r => r.Length));
        BinaryPrimitives.WriteUInt16BigEndian(record0.AsSpan(8), (ushort)textRecords.Length);
        BinaryPrimitives.WriteUInt16BigEndian(record0.AsSpan(10), 4096);
        Encoding.ASCII.GetBytes("MOBI").CopyTo(record0, 16);
        BinaryPrimitives.WriteUInt32BigEndian(record0.AsSpan(16 + 4), MobiHeaderLength);
        BinaryPrimitives.WriteUInt32BigEndian(record0.AsSpan(16 + 0x0C), encoding);
        BinaryPrimitives.WriteUInt16BigEndian(record0.AsSpan(0xF2), extraFlags);
        if (huffman is { } table)
        {
            // The Huffman header is at 0x70 from the start of record 0: the HUFF record's index, then the number of records.
            BinaryPrimitives.WriteUInt32BigEndian(record0.AsSpan(0x70), table.Index);
            BinaryPrimitives.WriteUInt32BigEndian(record0.AsSpan(0x74), table.Count);
        }

        List<byte[]> records = [record0, .. textRecords, .. trailingRecords];
        int offset = 78 + recordCount * 8;
        using MemoryStream file = new();
        byte[] header = new byte[78];
        Encoding.ASCII.GetBytes("Test book").CopyTo(header, 0);
        Encoding.ASCII.GetBytes("BOOK").CopyTo(header, 60);
        Encoding.ASCII.GetBytes("MOBI").CopyTo(header, 64);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(76), (ushort)recordCount);
        file.Write(header);
        foreach (byte[] record in records)
        {
            byte[] entry = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(entry, (uint)offset);
            file.Write(entry);
            offset += record.Length;
        }

        foreach (byte[] record in records)
        {
            file.Write(record);
        }

        return file.ToArray();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PalmDocText_IsDecompressed_AndItsMarkupReadAsLines()
    {
        // "abc" then a back-reference of 3 bytes at distance 3 gives "abcabc"; a space and "w" pair gives " w".
        byte[] text = [.. Literal("<p>abc"), 0x80, 0x18, .. Literal("</p><p>Hello"), 0xF7, .. Literal("orld!</p>")];

        List<string> lines = [.. MobiTextReader.Read(MakeMobi([text]))];

        CollectionAssert.AreEqual(new[] { "abcabc", "Hello world!" }, lines);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TrailingEntries_AreStripped_BeforeDecompression()
    {
        // Flag bit 1 declares one trailing entry: two bytes, the last of which has its top bit set and holds the size.
        byte[] text = [.. Literal("<p>Kept</p>"), 0x00, 0x82];

        List<string> lines = [.. MobiTextReader.Read(MakeMobi([text], extraFlags: 0x0002))];

        CollectionAssert.AreEqual(new[] { "Kept" }, lines);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UncompressedText_IsReadAsItIs()
    {
        byte[] text = Encoding.UTF8.GetBytes("<p>Plain Ünïcode</p>");

        List<string> lines = [.. MobiTextReader.Read(MakeMobi([text], compression: 1))];

        CollectionAssert.AreEqual(new[] { "Plain Ünïcode" }, lines);
    }

    /// <summary>
    /// A HUFF record and one CDIC record. The code table maps every 8-bit code to the dictionary entry of the same value,
    /// and the byte 0x00 is an entry that is itself compressed, so a text byte 0x00 decodes to the entry for 0x41 ("A").
    /// </summary>
    private static byte[][] HuffmanRecords()
    {
        // HUFF: the cache table at 24 (256 entries), the base table after it (32 entries, left empty here).
        byte[] huff = new byte[24 + 1024 + 256];
        Encoding.ASCII.GetBytes("HUFF").CopyTo(huff, 0);
        BinaryPrimitives.WriteUInt32BigEndian(huff.AsSpan(8), 24);
        BinaryPrimitives.WriteUInt32BigEndian(huff.AsSpan(12), 24 + 1024);
        for (int code = 0; code < 256; code++)
        {
            // Found in the first table, 8 bits long; the code is value minus the top byte, so value = 2 * code gives code.
            uint entry = ((uint)(2 * code) << 8) | 0x80 | 8;
            BinaryPrimitives.WriteUInt32BigEndian(huff.AsSpan(24 + code * 4), entry);
        }

        // CDIC: a 16-byte header, 256 two-byte offsets (relative to the end of the header), then 256 entries of 3 bytes.
        byte[] cdic = new byte[16 + 512 + 3 * 256];
        Encoding.ASCII.GetBytes("CDIC").CopyTo(cdic, 0);
        BinaryPrimitives.WriteUInt32BigEndian(cdic.AsSpan(4), 16);
        BinaryPrimitives.WriteUInt32BigEndian(cdic.AsSpan(8), 256);
        BinaryPrimitives.WriteUInt32BigEndian(cdic.AsSpan(12), 8);
        for (int code = 0; code < 256; code++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(cdic.AsSpan(16 + 2 * code), (ushort)(512 + 3 * code));
            int at = 16 + 512 + 3 * code;
            if (code == 0)
            {
                // Length 1 with the "compressed" flag clear: the one byte is itself a code, to be decoded again.
                BinaryPrimitives.WriteUInt16BigEndian(cdic.AsSpan(at), 0x0001);
                cdic[at + 2] = 0x41;
            }
            else
            {
                // Length 1 with the flag set: the entry is the byte itself.
                BinaryPrimitives.WriteUInt16BigEndian(cdic.AsSpan(at), 0x8001);
                cdic[at + 2] = (byte)code;
            }
        }

        return [huff, cdic];
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HuffmanCompression_DecodesTheDictionary_IncludingEntriesThatAreCompressedThemselves()
    {
        // The text is "<p>", a byte that decodes to "A", then "</p>", all written as the codes the table maps them to.
        byte[] text = [0x3C, 0x70, 0x3E, 0x00, 0x3C, 0x2F, 0x70, 0x3E];

        // Record 0, the text record (1), HUFF (2) and CDIC (3): the HUFF index is 2 and two records follow it.
        List<string> lines = [.. MobiTextReader.Read(MakeMobi([text], compression: 17480, huffman: (2, 2), trailingRecords: HuffmanRecords()))];

        CollectionAssert.AreEqual(new[] { "A" }, lines);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HuffmanCompression_WithoutItsTables_IsReportedNotSilentlyEmpty()
    {
        List<string> lines = [.. MobiTextReader.Read(MakeMobi([[0x3C, 0x70, 0x3E]], compression: 17480, huffman: (5, 1)))];

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the book:"), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ABookWithOnlyPictures_SaysItHasNoText()
    {
        byte[] text = Literal("<html><body><img src=\"page1.jpg\"/></body></html>");

        List<string> lines = [.. MobiTextReader.Read(MakeMobi([text]))];

        CollectionAssert.AreEqual(new[] { "[The book has no text to show (it is only pictures)]" }, lines);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFileThatIsNotPalmDb_IsReportedAsTooShortOrMissingItsHeader()
    {
        List<string> lines = [.. MobiTextReader.Read(Encoding.ASCII.GetBytes("just text"))];

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the book:"), string.Join("\n", lines));
    }
}

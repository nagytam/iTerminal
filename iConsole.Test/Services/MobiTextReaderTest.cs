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
    private static byte[] MakeMobi(byte[][] textRecords, ushort compression = 2, ushort extraFlags = 0, uint encoding = 65001)
    {
        int recordCount = textRecords.Length + 1;
        byte[] record0 = new byte[16 + MobiHeaderLength];
        BinaryPrimitives.WriteUInt16BigEndian(record0, compression);
        BinaryPrimitives.WriteUInt32BigEndian(record0.AsSpan(4), (uint)textRecords.Sum(r => r.Length));
        BinaryPrimitives.WriteUInt16BigEndian(record0.AsSpan(8), (ushort)textRecords.Length);
        BinaryPrimitives.WriteUInt16BigEndian(record0.AsSpan(10), 4096);
        Encoding.ASCII.GetBytes("MOBI").CopyTo(record0, 16);
        BinaryPrimitives.WriteUInt32BigEndian(record0.AsSpan(16 + 4), MobiHeaderLength);
        BinaryPrimitives.WriteUInt32BigEndian(record0.AsSpan(16 + 0x0C), encoding);
        BinaryPrimitives.WriteUInt16BigEndian(record0.AsSpan(16 + 0xF2), extraFlags);

        List<byte[]> records = [record0, .. textRecords];
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

    [TestMethod]
    public void PalmDocText_IsDecompressed_AndItsMarkupReadAsLines()
    {
        // "abc" then a back-reference of 3 bytes at distance 3 gives "abcabc"; a space and "w" pair gives " w".
        byte[] text = [.. Literal("<p>abc"), 0x80, 0x18, .. Literal("</p><p>Hello"), 0xF7, .. Literal("orld!</p>")];

        List<string> lines = [.. MobiTextReader.Read(MakeMobi([text]))];

        CollectionAssert.AreEqual(new[] { "abcabc", "Hello world!" }, lines);
    }

    [TestMethod]
    public void TrailingEntries_AreStripped_BeforeDecompression()
    {
        // Flag bit 1 declares one trailing entry: two bytes, the last of which has its top bit set and holds the size.
        byte[] text = [.. Literal("<p>Kept</p>"), 0x00, 0x82];

        List<string> lines = [.. MobiTextReader.Read(MakeMobi([text], extraFlags: 0x0002))];

        CollectionAssert.AreEqual(new[] { "Kept" }, lines);
    }

    [TestMethod]
    public void UncompressedText_IsReadAsItIs()
    {
        byte[] text = Encoding.UTF8.GetBytes("<p>Plain Ünïcode</p>");

        List<string> lines = [.. MobiTextReader.Read(MakeMobi([text], compression: 1))];

        CollectionAssert.AreEqual(new[] { "Plain Ünïcode" }, lines);
    }

    [TestMethod]
    public void HuffmanCompression_IsReportedNotRead()
    {
        List<string> lines = [.. MobiTextReader.Read(MakeMobi([[0x00]], compression: 17480))];

        Assert.IsTrue(lines.Single().Contains("Huffman"), string.Join("\n", lines));
    }

    [TestMethod]
    public void AFileThatIsNotPalmDb_IsReportedAsTooShortOrMissingItsHeader()
    {
        List<string> lines = [.. MobiTextReader.Read(Encoding.ASCII.GetBytes("just text"))];

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the book:"), string.Join("\n", lines));
    }
}

using System.IO.Compression;
using System.Text;
using iConsole.IWork;

namespace iConsole.Test.IWork;

/// <summary>
/// Reading an iWork document. The archives are built here: a Snappy block of literals, the protobuf messages of a Pages
/// storage or a Numbers sheet, table and string list, in .iwa chunks inside a zip. The real Numbers file in TestFiles
/// (issue-18.numbers, MIT-licensed, from masaccio/numbers-parser) exercises the Snappy back-references the builders skip.
/// </summary>
[TestClass]
public class IWorkTextReaderTest
{
    private const uint PagesStorage = 2001;
    private const uint SheetType = 2;
    private const uint TableInfoType = 6000;
    private const uint TableModelType = 6001;
    private const uint TableDataListType = 6005;

    // ── encoders ────────────────────────────────────────────────────────────

    private static void Varint(List<byte> output, ulong value)
    {
        while (value >= 0x80)
        {
            output.Add((byte)(value | 0x80));
            value >>= 7;
        }

        output.Add((byte)value);
    }

    private static void Bytes(List<byte> output, int field, byte[] data)
    {
        Varint(output, (ulong)(field << 3 | 2));
        Varint(output, (ulong)data.Length);
        output.AddRange(data);
    }

    private static void Text(List<byte> output, int field, string text) => Bytes(output, field, Encoding.UTF8.GetBytes(text));

    private static void Number(List<byte> output, int field, ulong value)
    {
        Varint(output, (ulong)(field << 3));
        Varint(output, value);
    }

    private static byte[] Reference(ulong identifier)
    {
        List<byte> reference = [];
        Number(reference, 1, identifier);
        return [.. reference];
    }

    /// <summary>A Snappy block made of literals only: the uncompressed length, then runs of up to 256 bytes.</summary>
    private static byte[] SnappyLiterals(byte[] data)
    {
        List<byte> block = [];
        Varint(block, (ulong)data.Length);
        for (int i = 0; i < data.Length;)
        {
            int run = Math.Min(256, data.Length - i);
            if (run <= 60)
            {
                block.Add((byte)((run - 1) << 2));
            }
            else
            {
                block.Add(60 << 2);
                block.Add((byte)(run - 1));
            }

            block.AddRange(data.AsSpan(i, run).ToArray());
            i += run;
        }

        return [.. block];
    }

    /// <summary>An .iwa file: one Snappy chunk holding an archive per object, each with its ArchiveInfo and payload.</summary>
    private static byte[] IwaFile(params (ulong Identifier, uint Type, byte[] Payload)[] objects)
    {
        List<byte> plain = [];
        foreach ((ulong identifier, uint type, byte[] payload) in objects)
        {
            List<byte> messageInfo = [];
            Number(messageInfo, 1, type);
            Number(messageInfo, 3, (ulong)payload.Length);
            List<byte> info = [];
            Number(info, 1, identifier);
            Bytes(info, 2, [.. messageInfo]);
            Varint(plain, (ulong)info.Count);
            plain.AddRange(info);
            plain.AddRange(payload);
        }

        byte[] block = SnappyLiterals([.. plain]);
        List<byte> file = [0x00, (byte)block.Length, (byte)(block.Length >> 8), (byte)(block.Length >> 16)];
        file.AddRange(block);
        return [.. file];
    }

    private static byte[] Storage(int kind, params string[] paragraphs)
    {
        List<byte> storage = [];
        Number(storage, 1, (ulong)kind);
        foreach (string paragraph in paragraphs)
        {
            Text(storage, 3, paragraph);
        }

        return [.. storage];
    }

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using MemoryStream memory = new();
        using (ZipArchive zip = new(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in entries)
            {
                using Stream stream = zip.CreateEntry(name).Open();
                stream.Write(content);
            }
        }

        return memory.ToArray();
    }

    private static IReadOnlyList<string> Read(byte[] file) => IWorkTextReader.Read(file);

    // ── Pages and Keynote ───────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void APagesDocument_ShowsItsBodyParagraphsThenItsOtherStorages()
    {
        byte[] document = Zip(("Index/Document.iwa", IwaFile(
            (5, PagesStorage, Storage(1, "Header text")),
            (6, PagesStorage, Storage(0, "First paragraph.\nSecond paragraph.")))));

        CollectionAssert.AreEqual(
            new[] { "First paragraph.", "Second paragraph.", "", "Header text" },
            Read(document).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AttachmentCharacters_AreLeftOutOfTheText()
    {
        byte[] document = Zip(("Index/Document.iwa", IwaFile((5, PagesStorage, Storage(0, "Before￼after")))));

        CollectionAssert.AreEqual(new[] { "Beforeafter" }, Read(document).ToArray());
    }

    // ── Numbers ─────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void ANumbersDocument_ListsTheTextCellsOfEachTableUnderItsSheet()
    {
        List<byte> entries = [];
        foreach (string text in new[] { "Item A", "Item B" })
        {
            List<byte> entry = [];
            Number(entry, 1, 1);
            Number(entry, 2, 1);
            Text(entry, 3, text);
            Bytes(entries, 3, [.. entry]);
        }

        List<byte> list = [];
        Number(list, 1, 1);
        Number(list, 2, 3);
        list.AddRange(entries);

        List<byte> dataStore = [];
        Bytes(dataStore, 4, Reference(40));
        List<byte> model = [];
        Bytes(model, 4, [.. dataStore]);
        List<byte> info = [];
        Bytes(info, 2, Reference(30));
        List<byte> sheet = [];
        Text(sheet, 1, "Budget");
        Bytes(sheet, 2, Reference(20));

        byte[] document = Zip(("Index/Document.iwa", IwaFile(
            (10, SheetType, [.. sheet]),
            (20, TableInfoType, [.. info]),
            (30, TableModelType, [.. model]),
            (40, TableDataListType, [.. list]))));

        IReadOnlyList<string> lines = Read(document);

        Assert.AreEqual("Numeric and formula cells are not read; the text cells of each table are listed under its sheet.", lines[0]);
        Assert.IsTrue(lines.Any(l => l.StartsWith("── Sheet: Budget")), string.Join("\n", lines));
        CollectionAssert.Contains(lines.ToArray(), "Table 1");
        CollectionAssert.Contains(lines.ToArray(), "  Item A");
        CollectionAssert.Contains(lines.ToArray(), "  Item B");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARealNumbersFile_IsReadWithItsSheetsAndCells()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Services", "TestFiles", "issue-18.numbers");

        IReadOnlyList<string> lines = Read(File.ReadAllBytes(path));

        Assert.IsTrue(lines.Any(l => l.StartsWith("── Sheet: Sheet 1")), string.Join("\n", lines));
        CollectionAssert.Contains(lines.ToArray(), "  SheetJS");
        CollectionAssert.Contains(lines.ToArray(), "  B3:D3");
    }

    // ── failures ────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void AZipWithNoIwork_SaysNoIWorkContentWasFound()
    {
        byte[] document = Zip(("readme.txt", Encoding.UTF8.GetBytes("hello")));

        CollectionAssert.AreEqual(
            new[] { "[Could not read the document: no iWork content was found, or it is encrypted]" },
            Read(document).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnIWork09Document_SaysItIsNotRead()
    {
        byte[] document = Zip(("index.xml", Encoding.UTF8.GetBytes("<document/>")));

        CollectionAssert.AreEqual(new[] { "[This is an iWork '09 document, which is not read]" }, Read(document).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnArchiveThatDoesNotDecompress_IsLeftOutRatherThanFailingTheDocument()
    {
        byte[] damaged = [0x07, 0x01, 0x00, 0x00];
        byte[] document = Zip(("Index/Document.iwa", damaged));

        Assert.IsTrue(Read(document).Single().StartsWith("[Could not read the document: no iWork content"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsZip_RecognisesTheZipSignatureOnly()
    {
        Assert.IsTrue(IWorkTextReader.IsZip(Zip(("a.txt", [1]))));
        Assert.IsFalse(IWorkTextReader.IsZip(Encoding.ASCII.GetBytes("plain key file")));
    }
}

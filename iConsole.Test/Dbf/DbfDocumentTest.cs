using System.Text;
using iConsole.Dbf;
using iConsole.Test.Common;

namespace iConsole.Test.Dbf;

[TestClass]
public class DbfDocumentTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Parse_TheExampleTableReadsItsFieldsAndLiveRecords()
    {
        DbfDocument document = DbfDocument.Parse(SampleFiles.Bytes("example.dbf"));

        Assert.AreEqual(3, document.Version);
        Assert.AreEqual(new DateOnly(2026, 3, 5), document.LastUpdated);
        CollectionAssert.AreEqual(new[] { "NAME", "AMOUNT", "DATE", "PAID", "NOTE" }, document.Fields.Select(f => f.Name).ToArray());
        Assert.AreEqual(3, document.Rows.Count, "the deleted record is not one of the rows");
        Assert.AreEqual(1, document.DeletedCount);
        Assert.AreEqual("windows-1252", document.CodePage.WebName);
        Assert.IsFalse(document.Truncated);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Parse_EachTypeReadsAsText()
    {
        DbfDocument document = DbfDocument.Parse(SampleFiles.Bytes("example.dbf"));

        string[] first = document.Rows[0];
        Assert.AreEqual("Pécs office", first[0], "a character field keeps its accents and loses its trailing spaces");
        Assert.AreEqual("1234.50", first[1], "a number field is trimmed");
        Assert.AreEqual("2026-03-05", first[2], "a date reads as ISO text");
        Assert.AreEqual("true", first[3], "a logical T reads as true");
        Assert.AreEqual("", first[4], "a memo with no block number is empty");

        Assert.AreEqual("-20.25", document.Rows[1][1]);
        Assert.AreEqual("false", document.Rows[1][3]);
        Assert.AreEqual("(memo)", document.Rows[1][4], "a memo block is named, its text is not read");

        string[] last = document.Rows[2];
        Assert.AreEqual("Szeged", last[0]);
        Assert.AreEqual("", last[2], "an empty date is empty");
        Assert.AreEqual("", last[3], "a logical ? is empty");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Parse_TooShortToHoldAHeaderIsRefused()
    {
        Assert.ThrowsExactly<FormatException>(() => DbfDocument.Parse(new byte[10]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Parse_AHeaderLengthPastTheEndOfTheFileIsRefused()
    {
        byte[] bytes = SampleFiles.Bytes("example.dbf");
        bytes[8] = 0xFF;
        bytes[9] = 0xFF;

        Assert.ThrowsExactly<FormatException>(() => DbfDocument.Parse(bytes));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Parse_AFileWithNoFieldsIsRefused()
    {
        byte[] bytes = new byte[64];
        bytes[8] = 33;
        bytes[10] = 1;

        Assert.ThrowsExactly<FormatException>(() => DbfDocument.Parse(bytes));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Parse_ATableCutShortKeepsItsCompleteRecords()
    {
        byte[] full = SampleFiles.Bytes("example.dbf");
        int headerLength = BitConverter.ToUInt16(full, 8);
        int recordLength = BitConverter.ToUInt16(full, 10);
        byte[] cut = full[..(headerLength + recordLength + 5)];

        DbfDocument document = DbfDocument.Parse(cut);

        Assert.AreEqual(1, document.Rows.Count, "only the first record is whole");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Parse_AFieldOfAnUnknownTypeReadsAsItsLetter()
    {
        byte[] bytes = BuildTable('Q', 4, "1234");

        DbfDocument document = DbfDocument.Parse(bytes);

        Assert.AreEqual("[Q]", document.Rows[0][0]);
    }

    /// <summary>A one-field table with one live record.</summary>
    private static byte[] BuildTable(char type, int length, string value)
    {
        int headerLength = 32 + 32 + 1;
        int recordLength = 1 + length;
        var bytes = new byte[headerLength + recordLength];
        bytes[0] = 0x03;
        bytes[4] = 1;
        BitConverter.GetBytes((ushort)headerLength).CopyTo(bytes, 8);
        BitConverter.GetBytes((ushort)recordLength).CopyTo(bytes, 10);
        Encoding.ASCII.GetBytes("F1").CopyTo(bytes, 32);
        bytes[32 + 11] = (byte)type;
        bytes[32 + 16] = (byte)length;
        bytes[64] = 0x0D;
        bytes[headerLength] = (byte)' ';
        Encoding.ASCII.GetBytes(value.PadRight(length)).CopyTo(bytes, headerLength + 1);
        return bytes;
    }
}

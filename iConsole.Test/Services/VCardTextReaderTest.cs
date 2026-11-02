using System.Text;
using iConsole.Services;

namespace iConsole.Test.Services;

/// <summary>Reading a vCard address book: one block per contact, with structured values spelled out.</summary>
[TestClass]
public class VCardTextReaderTest
{
    private static IReadOnlyList<string> Read(string text) => VCardTextReader.Read(Encoding.UTF8.GetBytes(text));

    [TestMethod]
    public void EachContact_IsAHeadedBlockOfItsProperties()
    {
        const string card = """
            BEGIN:VCARD
            VERSION:3.0
            N:Doe;Jane;;Dr.;
            FN:Jane Doe
            ORG:ACME Corp;Research
            TEL;TYPE=WORK,VOICE:+1 555 0100
            ADR;TYPE=HOME:;;1 Main St;Springfield;IL;62701;USA
            NOTE:Met at the conference\, follow up
            END:VCARD
            """;

        CollectionAssert.AreEqual(
            new[]
            {
                "Jane Doe",
                "--------",
                "Organization: ACME Corp, Research",
                "Phone: +1 555 0100 (work, voice)",
                "Address: 1 Main St, Springfield, IL, 62701, USA (home)",
                "Note: Met at the conference, follow up",
            },
            Read(card).ToArray());
    }

    [TestMethod]
    public void AContactWithoutAFormattedName_IsNamedFromTheStructuredName()
    {
        IReadOnlyList<string> lines = Read("BEGIN:VCARD\r\nN:Roe;Richard;;;\r\nEMAIL:richard@example.com\r\nEND:VCARD\r\n");

        Assert.AreEqual("Richard Roe", lines[0]);
        Assert.AreEqual("Email: richard@example.com", lines[^1]);
    }

    [TestMethod]
    public void AFoldedLine_IsJoinedBeforeItIsRead()
    {
        IReadOnlyList<string> lines = Read("BEGIN:VCARD\nFN:Jane\n  Doe\nEND:VCARD\n");

        Assert.AreEqual("Jane Doe", lines[0]);
    }

    [TestMethod]
    public void AGroupedProperty_IsShownUnderItsBareName()
    {
        IReadOnlyList<string> lines = Read("BEGIN:VCARD\nFN:Jane Doe\nitem1.URL:https://example.com\nEND:VCARD\n");

        Assert.IsTrue(lines.Contains("Web: https://example.com"), string.Join("\n", lines));
    }

    [TestMethod]
    public void APhoto_IsNotShownAsItsBase64Text()
    {
        IReadOnlyList<string> lines = Read("BEGIN:VCARD\nFN:Richard Roe\nPHOTO;ENCODING=b;TYPE=JPEG:/9j/4AAQSkZJRgABAQ==\nEND:VCARD\n");

        Assert.IsTrue(lines.Contains("Photo: [embedded image]"), string.Join("\n", lines));
        Assert.IsFalse(lines.Any(l => l.Contains("/9j/")));
    }

    [TestMethod]
    public void TwoContacts_AreSeparatedByABlankLine()
    {
        IReadOnlyList<string> lines = Read("BEGIN:VCARD\nFN:A\nEND:VCARD\nBEGIN:VCARD\nFN:B\nEND:VCARD\n");

        CollectionAssert.AreEqual(new[] { "A", "---", "", "B", "---" }, lines.ToArray());
    }

    [TestMethod]
    public void AFileWithNoContacts_SaysSo()
    {
        CollectionAssert.AreEqual(new[] { "[No contacts were found in this file]" }, Read("just some text\n").ToArray());
    }
}

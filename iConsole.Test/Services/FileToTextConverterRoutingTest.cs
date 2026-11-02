using System.IO.Compression;
using System.Text;
using iConsole.Data;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Test.Services;

/// <summary>
/// The F3 viewer's routing for the formats added with the iWork, Access, calendar, mailbox and OpenDocument readers: each
/// file is written to a temporary folder and read through <see cref="FileToTextConverterService"/>, as the viewer reads it.
/// </summary>
[TestClass]
public class FileToTextConverterRoutingTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void CreateFolder() => _folder = Path.Combine(Path.GetTempPath(), "iTerminal-routing-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void DeleteFolder()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private async Task<IReadOnlyList<string>> ReadAsync(string name)
    {
        Directory.CreateDirectory(_folder);
        var model = new TextModel();
        await new FileToTextConverterService().ReadFileAsync(new Url(Path.Combine(_folder, name)), new LocalFileSystemAdapter(), model);
        return [.. model.Lines];
    }

    private void Write(string name, string text)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, name), text, Encoding.UTF8);
    }

    private void WriteZip(string name, params (string Entry, string Content)[] entries)
    {
        Directory.CreateDirectory(_folder);
        using ZipArchive zip = ZipFile.Open(Path.Combine(_folder, name), ZipArchiveMode.Create);
        foreach ((string entry, string content) in entries)
        {
            using Stream stream = zip.CreateEntry(entry).Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }
    }

    [TestMethod]
    public async Task Vcf_IsReadAsContacts()
    {
        Write("contacts.vcf", "BEGIN:VCARD\nFN:Jane Doe\nEND:VCARD\n");

        CollectionAssert.AreEqual(new[] { "Jane Doe", "--------" }, (await ReadAsync("contacts.vcf")).ToArray());
    }

    [TestMethod]
    public async Task Ics_IsReadAsCalendarItems()
    {
        Write("calendar.ics", "BEGIN:VCALENDAR\nX-WR-CALNAME:Work\nBEGIN:VEVENT\nSUMMARY:Standup\nEND:VEVENT\nEND:VCALENDAR\n");

        IReadOnlyList<string> lines = await ReadAsync("calendar.ics");

        Assert.AreEqual("Calendar: Work", lines[0]);
        CollectionAssert.Contains(lines.ToArray(), "Event: Standup");
    }

    [TestMethod]
    public async Task Mbox_IsReadAsMessages()
    {
        Write("mail.mbox", "From a@example.com Mon Mar  2 09:00:00 2026\nSubject: Hello\n\nBody text\n");

        Assert.AreEqual("Message 1 of 1", (await ReadAsync("mail.mbox"))[0]);
    }

    [TestMethod]
    public async Task Log_IsPlainTextAsBefore()
    {
        Write("server.log", "2026-03-01 10:00:01 INFO Server started\n");

        Assert.AreEqual("2026-03-01 10:00:01 INFO Server started", (await ReadAsync("server.log"))[0]);
    }

    [TestMethod]
    public async Task Odf_IsReadAsAFormula()
    {
        WriteZip("formula.odf", ("content.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:math="http://www.w3.org/1998/Math/MathML">
            <office:body><office:math><math:math><math:mi>x</math:mi><math:mo>=</math:mo><math:mn>2</math:mn></math:math></office:math></office:body>
            </office:document-content>
            """));

        IReadOnlyList<string> lines = await ReadAsync("formula.odf");

        Assert.IsTrue(lines.Any(l => l.Contains("Formula")), string.Join("\n", lines));
        CollectionAssert.Contains(lines.ToArray(), "x = 2");
    }

    [TestMethod]
    public async Task Odm_ShowsItsSectionsFromTheSameFolder()
    {
        WriteZip("book.odm", ("content.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" xmlns:xlink="http://www.w3.org/1999/xlink">
            <office:body><office:text><text:section text:name="Sec"><text:section-source xlink:href="chapter%201.odt"/></text:section></office:text></office:body>
            </office:document-content>
            """));
        WriteZip("chapter 1.odt", ("content.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">
            <office:body><office:text><text:p>The first chapter says hello.</text:p></office:text></office:body>
            </office:document-content>
            """));

        IReadOnlyList<string> lines = await ReadAsync("book.odm");

        Assert.IsTrue(lines.Any(l => l.Contains("chapter 1.odt")), string.Join("\n", lines));
        CollectionAssert.Contains(lines.ToArray(), "The first chapter says hello.");
    }

    [TestMethod]
    public async Task Odm_ReportsALinkedDocumentThatIsNotThere()
    {
        WriteZip("missing.odm", ("content.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" xmlns:xlink="http://www.w3.org/1999/xlink">
            <office:body><office:text><text:section text:name="Sec"><text:section-source xlink:href="gone.odt"/></text:section></office:text></office:body>
            </office:document-content>
            """));

        CollectionAssert.Contains((await ReadAsync("missing.odm")).ToArray(), "[The linked document is not in this folder]");
    }

    [TestMethod]
    public async Task Key_ThatIsNotAZip_IsShownAsPlainTextAsBefore()
    {
        Write("signing.key", "-----BEGIN PRIVATE KEY-----\n");

        Assert.AreEqual("-----BEGIN PRIVATE KEY-----", (await ReadAsync("signing.key"))[0]);
    }

    [TestMethod]
    public async Task Pages_ThatIsAZipWithNoIWorkContent_SaysSo()
    {
        WriteZip("letter.pages", ("readme.txt", "nothing here"));

        IReadOnlyList<string> lines = await ReadAsync("letter.pages");

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the document:"), string.Join("\n", lines));
    }

    [TestMethod]
    public async Task Mdb_ThatIsNotAccess_SaysSo()
    {
        Write("notes.mdb", "not an access database");

        Assert.IsTrue((await ReadAsync("notes.mdb")).Single().StartsWith("[Could not read the database:"));
    }
}

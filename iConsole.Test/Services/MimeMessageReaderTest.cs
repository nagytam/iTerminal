using System.Text;
using iConsole.Services;

namespace iConsole.Test.Services;

/// <summary>Reading an .eml message: headers, RFC 2047 words, transfer encodings, charsets and multipart bodies.</summary>
[TestClass]
public class MimeMessageReaderTest
{
    private static List<string> Read(string message) => [.. MimeMessageReader.Read(Encoding.UTF8.GetBytes(message))];

    [Timeout(60_000)]
    [TestMethod]
    public void PlainMessage_ShowsItsAddressesDateSubjectAndBody()
    {
        List<string> lines = Read("""
            From: Ann <ann@example.org>
            To: Bob <bob@example.org>
            Subject: Hello
            Date: Mon, 1 Jan 2024 10:00:00 +0000

            Line one
            Line two

            """);

        CollectionAssert.Contains(lines, "From: Ann <ann@example.org>");
        CollectionAssert.Contains(lines, "To: Bob <bob@example.org>");
        CollectionAssert.Contains(lines, "Subject: Hello");
        CollectionAssert.Contains(lines, "Line one");
        CollectionAssert.Contains(lines, "Line two");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EncodedWords_AreDecoded_AndFoldedHeadersJoin()
    {
        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("Grüße aus Zürich"));
        List<string> lines = Read($"""
            Subject: =?UTF-8?B?{base64}?=
            From: =?ISO-8859-1?Q?Caf=E9_Ann?= <ann@example.org>
            To: first@example.org,
             second@example.org

            Body
            """);

        CollectionAssert.Contains(lines, "Subject: Grüße aus Zürich");
        CollectionAssert.Contains(lines, "From: Café Ann <ann@example.org>");
        CollectionAssert.Contains(lines, "To: first@example.org, second@example.org");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void QuotedPrintableBody_IsDecoded_AndSoftBreaksJoin()
    {
        List<string> lines = Read("""
            Content-Type: text/plain; charset=UTF-8
            Content-Transfer-Encoding: quoted-printable

            Caf=C3=A9 soft=
            break
            """);

        CollectionAssert.Contains(lines, "Café softbreak");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Base64BodyInItsCharset_IsDecoded()
    {
        string base64 = Convert.ToBase64String(Encoding.Latin1.GetBytes("Ärger"));
        List<string> lines = Read($"""
            Content-Type: text/plain; charset="ISO-8859-1"
            Content-Transfer-Encoding: base64

            {base64}
            """);

        CollectionAssert.Contains(lines, "Ärger");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Multipart_PrefersThePlainPart_OverTheHtml()
    {
        List<string> lines = Read("""
            Subject: Choice
            Content-Type: multipart/alternative; boundary="b1"

            --b1
            Content-Type: text/html; charset=utf-8

            <p>HTML version</p>
            --b1
            Content-Type: text/plain; charset=utf-8

            Plain version
            --b1--
            """);

        CollectionAssert.Contains(lines, "Plain version");
        Assert.IsFalse(lines.Any(l => l.Contains("HTML version")), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HtmlOnlyMessage_ShowsTheHtmlAsText()
    {
        List<string> lines = Read("""
            Content-Type: text/html; charset=utf-8

            <p>Hello <b>there</b></p>
            """);

        CollectionAssert.Contains(lines, "Hello there");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Attachments_AreListedByName_NotShown()
    {
        List<string> lines = Read("""
            Content-Type: multipart/mixed; boundary="outer"

            --outer
            Content-Type: text/plain; charset=utf-8

            See attached.
            --outer
            Content-Type: application/pdf; name="report.pdf"
            Content-Disposition: attachment; filename="report.pdf"
            Content-Transfer-Encoding: base64

            JVBERi0=
            --outer--
            """);

        CollectionAssert.Contains(lines, "See attached.");
        CollectionAssert.Contains(lines, "[Attachment: report.pdf]");
        Assert.IsFalse(lines.Any(l => l.Contains("JVBER")), string.Join("\n", lines));
    }
}

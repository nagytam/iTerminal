using iTerminal.Core.Model.Mail;

namespace iTerminal.Core.Test.Model.Mail;

[TestClass]
public class MimeParserTest
{
    [TestMethod]
    public void APlainMessageGivesItsHeadersAndBody()
    {
        const string raw = "From: Ada <ada@example.test>\r\nTo: you@example.test\r\nSubject: Hello\r\nDate: Tue, 14 Oct 2026 09:00:00 +0200\r\n\r\nLine one\r\nLine two";

        var item = MimeParser.Parse(3, raw);

        Assert.AreEqual(3, item.Number);
        Assert.AreEqual("Ada <ada@example.test>", item.From);
        Assert.AreEqual("you@example.test", item.To);
        Assert.AreEqual("Hello", item.Subject);
        Assert.AreEqual("Line one\r\nLine two", item.Body);
    }

    [TestMethod]
    public void FoldedHeadersAreUnfolded()
    {
        const string raw = "Subject: A long\r\n subject that\r\n\tcontinues\r\n\r\nbody";

        Assert.AreEqual("A long subject that continues", MimeParser.Parse(1, raw).Subject);
    }

    [TestMethod]
    public void EncodedWordsInHeadersAreDecoded()
    {
        Assert.AreEqual("Árvíztűrő", MimeParser.DecodeWords("=?utf-8?B?w4FydsOtenTFsXLFkQ==?="));
        Assert.AreEqual("Grüße aus Köln", MimeParser.DecodeWords("=?utf-8?Q?Gr=C3=BC=C3=9Fe_aus_K=C3=B6ln?="));
    }

    [TestMethod]
    public void AdjacentEncodedWordsJoinWithoutTheSpaceBetween()
    {
        Assert.AreEqual("Ab", MimeParser.DecodeWords("=?utf-8?Q?A?= =?utf-8?Q?b?="));
    }

    [TestMethod]
    public void AMultipartMessagePrefersItsPlainTextPart()
    {
        const string raw =
            "Subject: Both\r\n" +
            "Content-Type: multipart/alternative; boundary=\"XYZ\"\r\n" +
            "\r\n" +
            "preamble\r\n" +
            "--XYZ\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "\r\n" +
            "<p>Hello <b>html</b></p>\r\n" +
            "--XYZ\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            "\r\n" +
            "Hello plain\r\n" +
            "--XYZ--\r\n";

        Assert.AreEqual("Hello plain", MimeParser.Parse(1, raw).Body);
    }

    [TestMethod]
    public void ABase64BodyIsDecodedWithItsCharset()
    {
        const string raw = "Content-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\nw4FydsOtenTFsXLFkQ==\r\n";

        Assert.AreEqual("Árvíztűrő", MimeParser.Parse(1, raw).Body);
    }

    [TestMethod]
    public void AQuotedPrintableBodyDecodesSoftBreaksAndBytes()
    {
        const string raw = "Content-Type: text/plain\r\nContent-Transfer-Encoding: quoted-printable\r\n\r\nGr=C3=BC=C3=9Fe, this line =\r\ncontinues.";

        Assert.AreEqual("Grüße, this line continues.", MimeParser.Parse(1, raw).Body);
    }

    [TestMethod]
    public void AnHtmlOnlyMessageShowsItsTextWithoutTags()
    {
        const string raw = "Content-Type: text/html\r\n\r\n<html><style>p{}</style><p>Hello&nbsp;&amp; <b>bye</b></p><br>Next</html>";

        var body = MimeParser.Parse(1, raw).Body;

        StringAssert.Contains(body, "Hello & bye");
        StringAssert.Contains(body, "Next");
        Assert.IsFalse(body.Contains('<'));
        Assert.IsFalse(body.Contains("p{}"));
    }

    [TestMethod]
    public void AttachmentsAreNotTakenForTheBody()
    {
        const string raw =
            "Content-Type: multipart/mixed; boundary=B\r\n\r\n" +
            "--B\r\nContent-Type: text/plain\r\nContent-Disposition: attachment; filename=notes.txt\r\n\r\nattached text\r\n" +
            "--B\r\nContent-Type: text/plain\r\n\r\nthe real body\r\n--B--\r\n";

        Assert.AreEqual("the real body", MimeParser.Parse(1, raw).Body);
    }

    [TestMethod]
    public void TheListLineNeedsOnlyTheHeaders()
    {
        var listing = MimeParser.ParseListing(9, 1234, "From: Grace <grace@example.test>\r\nSubject: Compilers\r\nDate: Wed, 15 Oct 2026 08:00:00 +0000");

        Assert.AreEqual(new MailListing(9, 1234, "Grace <grace@example.test>", "Compilers", "Wed, 15 Oct 2026 08:00:00 +0000"), listing);
    }
}

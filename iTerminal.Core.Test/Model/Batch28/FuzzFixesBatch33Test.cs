using iTerminal.Core.Documents.Extractors;
using iTerminal.Core.Helper;
using iTerminal.Core.Model.Mail;
using iTerminal.Core.Model.Web;
using iTerminal.Core.Services.Documents;
using iTerminal.Core.Services.Mail;

namespace iTerminal.Core.Test.Model.Batch28;

/// <summary>Readers and path helpers that threw for null or for a cut-off reply (found by fuzzing).</summary>
[TestClass]
public class FuzzFixesBatch33Test
{
    [Timeout(60_000)]
    [TestMethod]
    public void Ber_Elements_OfACutOffLength_IsAFormatException_NotAnIndexError()
    {
        Assert.ThrowsExactly<FormatException>(() => Ber.Elements([0x30]));
        Assert.ThrowsExactly<FormatException>(() => Ber.Elements([0x30, 0x84, 0x01]));
        Assert.ThrowsExactly<FormatException>(() => Ber.Elements([0x30, 0x84, 0xFF, 0xFF, 0xFF, 0xFF, 0x01]));
        Assert.ThrowsExactly<FormatException>(() => Ber.Elements([0x30, 0x89, 1, 2, 3, 4, 5, 6, 7, 8, 9]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Ber_Elements_AndDecodeInt_AcceptNull()
    {
        Assert.AreEqual(0, Ber.Elements(null).Count);
        Assert.AreEqual(0L, Ber.DecodeInt(null));
        Assert.AreEqual(1, Ber.Elements([0x02, 0x01, 0x05]).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MarkupTextExtractor_StripTagsAndCollapseWhitespace_AcceptNull()
    {
        Assert.AreEqual(string.Empty, MarkupTextExtractor.StripTags(null));
        Assert.AreEqual(string.Empty, MarkupTextExtractor.CollapseWhitespace(null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WebAddress_Helpers_AcceptNull()
    {
        Assert.AreEqual(string.Empty, WebAddress.WithoutFragment(null));
        Assert.AreEqual(string.Empty, WebAddress.Fragment(null));
        Assert.IsNotNull(WebAddress.FileUrl(null));
        Assert.IsNotNull(WebAddress.Resolve(null, null));
        Assert.AreEqual(string.Empty, WebDocument.Collapse(null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UrlPath_AcceptsNull()
    {
        Assert.AreEqual(string.Empty, UrlPath.TrimFolder(null));
        Assert.AreEqual('/', UrlPath.SeparatorOf(null));
        Assert.IsNotNull(UrlPath.Child(null, null, isFolder: true));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PasteHelper_SingleLine_OfNull_IsEmpty()
    {
        Assert.AreEqual(string.Empty, PasteHelper.SingleLine(null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OfficeAndPlainExtractors_AcceptNull()
    {
        Assert.AreEqual(0, OfficeTextExtractor.TrailingNumber(null));
        Assert.AreEqual(string.Empty, PlainTextExtractor.Decode(null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DocumentSearchIndex_BuildMatchExpression_SkipsNullTokens()
    {
        Assert.AreEqual(string.Empty, DocumentSearchIndex.BuildMatchExpression(null));
        Assert.AreEqual("\"a\"*", DocumentSearchIndex.BuildMatchExpression([null, "a", string.Empty]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MimeParser_SplitHeaders_OfNull_IsEmpty()
    {
        Assert.AreEqual((string.Empty, string.Empty), MimeParser.SplitHeaders(null));
    }
}

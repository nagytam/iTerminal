using iTerminal.Core.Documents.Extractors;
using iTerminal.Core.Model.Mail;
using iTerminal.Core.Model.Web;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Services.Clipboard;
using iTerminal.Core.Services.Documents;
using iTerminal.Core.Services.UserMenu;

namespace iTerminal.Core.Test.Model.Batch28;

/// <summary>Parsers and formatters that threw for a null text, a stray brace or a huge radius (found by fuzzing).</summary>
[TestClass]
public class FuzzFixesBatch32Test
{
    [Timeout(60_000)]
    [TestMethod]
    public void WebAddress_FromInput_WithABrokenSearchTemplate_UsesTheDefaultSearch()
    {
        string url = WebAddress.FromInput("hello world", "https://search.test/?q={");

        StringAssert.Contains(url, "hello+world");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WebAddress_FromInput_OfNull_IsBlank()
    {
        Assert.AreEqual(WebAddress.Blank, WebAddress.FromInput(null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WebAddress_IsSameDocument_WithNulls_IsTrueForTwoNulls()
    {
        Assert.IsTrue(WebAddress.IsSameDocument(null, null));
        Assert.IsFalse(WebAddress.IsSameDocument(null, "https://a.test/"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DocumentSearchIndex_BuildSnippet_WithAHugeRadius_ShowsTheWholeText()
    {
        Assert.AreEqual("some text here", DocumentSearchIndex.BuildSnippet("some text here", "text", int.MaxValue));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ManagedArchiveWriter_NormalizeName_OfNull_IsEmpty()
    {
        Assert.AreEqual(string.Empty, ManagedArchiveWriter.NormalizeName(null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HtmlParser_AndWebDocument_Parse_OfNullHtml_GiveAnEmptyDocument()
    {
        Assert.IsNotNull(HtmlParser.Parse(null));
        Assert.IsNotNull(WebDocument.Parse(null, null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MarkupTextExtractor_ExtractTitle_OfNull_IsEmpty()
    {
        Assert.AreEqual(string.Empty, MarkupTextExtractor.ExtractTitle(null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PdfTextExtractor_Helpers_AcceptNull()
    {
        Assert.AreEqual(string.Empty, PdfTextExtractor.FindInfoValue(null, null));
        Assert.AreEqual(0, PdfTextExtractor.CountPages(null));
        Assert.IsTrue(PdfTextExtractor.LooksLikeText(null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MimeParser_ParseHeaders_AndQuotedPrintable_AcceptNull()
    {
        Assert.AreEqual(0, MimeParser.ParseHeaders(null).Count);
        Assert.AreEqual(0, MimeParser.QuotedPrintable(null).Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UserMenuParser_Parse_OfNull_IsNoItems()
    {
        Assert.AreEqual(0, UserMenuParser.Parse(null).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WindowsTextClipboardService_ToClipboardBytes_OfNull_IsJustTheTerminator()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.AreEqual(2, WindowsTextClipboardService.ToClipboardBytes(null).Length);
        }
    }
}

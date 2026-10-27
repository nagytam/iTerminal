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
    [TestMethod]
    public void WebAddress_FromInput_WithABrokenSearchTemplate_UsesTheDefaultSearch()
    {
        string url = WebAddress.FromInput("hello world", "https://search.test/?q={");

        StringAssert.Contains(url, "hello+world");
    }

    [TestMethod]
    public void WebAddress_FromInput_OfNull_IsBlank()
    {
        Assert.AreEqual(WebAddress.Blank, WebAddress.FromInput(null));
    }

    [TestMethod]
    public void WebAddress_IsSameDocument_WithNulls_IsTrueForTwoNulls()
    {
        Assert.IsTrue(WebAddress.IsSameDocument(null, null));
        Assert.IsFalse(WebAddress.IsSameDocument(null, "https://a.test/"));
    }

    [TestMethod]
    public void DocumentSearchIndex_BuildSnippet_WithAHugeRadius_ShowsTheWholeText()
    {
        Assert.AreEqual("some text here", DocumentSearchIndex.BuildSnippet("some text here", "text", int.MaxValue));
    }

    [TestMethod]
    public void ManagedArchiveWriter_NormalizeName_OfNull_IsEmpty()
    {
        Assert.AreEqual(string.Empty, ManagedArchiveWriter.NormalizeName(null));
    }

    [TestMethod]
    public void HtmlParser_AndWebDocument_Parse_OfNullHtml_GiveAnEmptyDocument()
    {
        Assert.IsNotNull(HtmlParser.Parse(null));
        Assert.IsNotNull(WebDocument.Parse(null, null));
    }

    [TestMethod]
    public void MarkupTextExtractor_ExtractTitle_OfNull_IsEmpty()
    {
        Assert.AreEqual(string.Empty, MarkupTextExtractor.ExtractTitle(null));
    }

    [TestMethod]
    public void PdfTextExtractor_Helpers_AcceptNull()
    {
        Assert.AreEqual(string.Empty, PdfTextExtractor.FindInfoValue(null, null));
        Assert.AreEqual(0, PdfTextExtractor.CountPages(null));
        Assert.IsTrue(PdfTextExtractor.LooksLikeText(null));
    }

    [TestMethod]
    public void MimeParser_ParseHeaders_AndQuotedPrintable_AcceptNull()
    {
        Assert.AreEqual(0, MimeParser.ParseHeaders(null).Count);
        Assert.AreEqual(0, MimeParser.QuotedPrintable(null).Length);
    }

    [TestMethod]
    public void UserMenuParser_Parse_OfNull_IsNoItems()
    {
        Assert.AreEqual(0, UserMenuParser.Parse(null).Count);
    }

    [TestMethod]
    public void WindowsTextClipboardService_ToClipboardBytes_OfNull_IsJustTheTerminator()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.AreEqual(2, WindowsTextClipboardService.ToClipboardBytes(null).Length);
        }
    }
}

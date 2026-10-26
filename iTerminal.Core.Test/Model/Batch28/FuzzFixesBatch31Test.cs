using iTerminal.Core.Controls.Terminals;
using iTerminal.Core.Services.Documents;
using iTerminal.Core.Services.Remote;
using iTerminal.Core.Services.Stocks;
using iTerminal.Core.Services.UserMenu;

namespace iTerminal.Core.Test.Model.Batch28;

/// <summary>Services that threw for an empty reply, a null text or an absurd width (found by fuzzing).</summary>
[TestClass]
public class FuzzFixesBatch31Test
{
    [TestMethod]
    public void YahooStockQuoteProvider_ParseSearch_OfAnEmptyOrBrokenReply_IsNoHits()
    {
        Assert.AreEqual(0, YahooStockQuoteProvider.ParseSearch(string.Empty).Count);
        Assert.AreEqual(0, YahooStockQuoteProvider.ParseSearch("<html>502 Bad Gateway</html>").Count);
    }

    [TestMethod]
    public void YahooStockQuoteProvider_ParseChart_OfAnEmptyReply_IsNoQuote()
    {
        Assert.IsNull(YahooStockQuoteProvider.ParseChart("MSFT", string.Empty));
    }

    [TestMethod]
    public void DocumentSearchIndex_BuildSnippet_WithNullsOrANegativeRadius_DoesNotThrow()
    {
        Assert.AreEqual(string.Empty, DocumentSearchIndex.BuildSnippet(null, "x"));
        Assert.AreEqual("hello", DocumentSearchIndex.BuildSnippet("hello", null));
        Assert.IsTrue(DocumentSearchIndex.BuildSnippet("some text here", "text", -5).Contains("text"));
    }

    [TestMethod]
    public void PromptLine_Format_ToAnAbsurdWidth_IsCapped()
    {
        Assert.IsTrue(PromptLine.Format("$ ", "ls", int.MaxValue).Length <= 100_000);
    }

    [TestMethod]
    public void UserMenuMacros_Quote_OfNull_IsAnEmptyWord()
    {
        Assert.IsTrue(UserMenuMacros.Quote(null).Length == 2);
    }

    [TestMethod]
    public void RemoteShellLaunch_QuotePowerShell_OfNull_IsAnEmptyWord()
    {
        Assert.AreEqual("''", RemoteShellLaunch.QuotePowerShell(null));
    }
}

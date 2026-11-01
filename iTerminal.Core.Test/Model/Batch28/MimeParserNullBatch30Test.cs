using iTerminal.Core.Model.Mail;

namespace iTerminal.Core.Test.Model.Batch28;

/// <summary>A reply with no text at all must give an empty message, not a null reference.</summary>
[TestClass]
public class MimeParserNullBatch30Test
{
    [Timeout(60_000)]
    [TestMethod]
    public void Parse_OfNull_GivesAnEmptyItem()
    {
        MailItem item = MimeParser.Parse(3, null);

        Assert.AreEqual(3, item.Number);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ParseListing_OfNull_GivesAnEmptyListing()
    {
        MailListing listing = MimeParser.ParseListing(4, 10, null);

        Assert.AreEqual(4, listing.Number);
    }
}

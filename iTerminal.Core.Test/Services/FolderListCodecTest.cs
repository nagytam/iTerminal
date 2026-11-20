using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class FolderListCodecTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void AnEntryWithASemicolonOrAPercent_ReadsBackWhole()
    {
        string[] entries = ["/home/u/a;b", "/home/u/100%", "/home/u/%3B literal", "/home/u/plain"];

        CollectionAssert.AreEqual(entries, FolderListCodec.Split(FolderListCodec.Join(entries)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnEntryWithNeitherCharacter_IsStoredAsItAlwaysWas()
    {
        Assert.AreEqual("/a;/b", FolderListCodec.Join(["/a", "/b"]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void A_ValueSavedBeforeTheCodec_ReadsAsItDid()
    {
        CollectionAssert.AreEqual(new[] { "/a", "/b" }, FolderListCodec.Split("/a;/b"));
    }
}

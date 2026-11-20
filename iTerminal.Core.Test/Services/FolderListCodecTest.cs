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
    public void AValueSavedBeforeTheCodec_IsSplitWithoutUnescaping()
    {
        // A literal %25 or %3B in a path saved before the codec must read back as it was written.
        CollectionAssert.AreEqual(new[] { "/home/u/50%25off", "/tmp/%3B" }, FolderListCodec.Split("/home/u/50%25off;/tmp/%3B"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AValueWrittenByTheCodec_CarriesTheMarker()
    {
        Assert.IsTrue(FolderListCodec.Join(["/a"]).StartsWith("\u001E", StringComparison.Ordinal));
    }
}

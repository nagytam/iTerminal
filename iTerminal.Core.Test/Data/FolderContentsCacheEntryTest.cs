using iTerminal.Core.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Data.Test;

/// <summary>A cached folder listing starts empty rather than null, so a read of a fresh entry never fails.</summary>
[TestClass]
public class FolderContentsCacheEntryTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void A_NewEntry_HasEmptyPathAndContent()
    {
        var entry = new FolderContentsCacheEntry();

        Assert.AreEqual(string.Empty, entry.Path);
        Assert.AreEqual(string.Empty, entry.ContentJson);
    }
}

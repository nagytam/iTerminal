using iTerminal.Core.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Data.Test;

/// <summary>A cached folder-size entry starts at zero bytes with an empty path, so a fresh entry reads as "nothing measured yet".</summary>
[TestClass]
public class FolderIndexCacheEntryTest
{
    [TestMethod]
    public void A_NewEntry_HasEmptyPathAndZeroSize()
    {
        var entry = new FolderIndexCacheEntry();

        Assert.AreEqual(string.Empty, entry.Path);
        Assert.AreEqual(0L, entry.Size);
    }
}

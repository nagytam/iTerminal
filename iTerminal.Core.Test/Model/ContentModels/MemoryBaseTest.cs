using iConsole.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Model.ContentModels.Test;

/// <summary>Every item of the memory drive keeps its address and the moment it was made, so its date can be shown.</summary>
[TestClass]
public class MemoryBaseTest
{
    [TestMethod]
    public void Constructor_KeepsTheUrl_AndStampsTheCreationTime()
    {
        var url = new Url(iConsole.Data.Container.MEMORY_DRIVE + "a.txt");
        var before = DateTime.Now;

        var item = new MemoryBase(url);

        Assert.AreEqual(url.FullUrl, item.Url.FullUrl);
        Assert.IsTrue(item.CreationTime >= before && item.CreationTime <= DateTime.Now.AddSeconds(1));
    }
}

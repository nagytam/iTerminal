using iConsole.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Model.ContentModels.Test;

[TestClass]
public class MemoryFileTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void FromAStream_KeepsTheBytesAndTheMemoryDriveUrl()
    {
        var file = new MemoryFile("a.txt", new MemoryStream([1, 2, 3]));

        Assert.AreEqual("a.txt", file.FileName);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, file.Content);
        Assert.AreEqual(Container.MEMORY_DRIVE + "a.txt", file.Url.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FromAnArray_KeepsTheArray()
    {
        var file = new MemoryFile("b.bin", new byte[] { 9 });

        CollectionAssert.AreEqual(new byte[] { 9 }, file.Content);
    }
}

using iConsole.Data;
using iTerminal.Core.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Test.Commands;

/// <summary>The rule that sends a recursive local folder delete to the Trash whole, rather than file by file.</summary>
[TestClass]
public class WholeFolderToTrashTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("/home/u/tree/", true, true, ContainerType.FileSystem, true, DisplayName = "local folder, recursive, bin ticked")]
    [DataRow("/home/u/file.txt", true, true, ContainerType.FileSystem, false, DisplayName = "a file is not a folder")]
    [DataRow("/home/u/tree/", false, true, ContainerType.FileSystem, false, DisplayName = "non-recursive keeps its own rule")]
    [DataRow("/home/u/tree/", true, false, ContainerType.FileSystem, false, DisplayName = "bin unticked keeps the walk")]
    [DataRow("memdrive://box/", true, true, ContainerType.MemoryDrive, false, DisplayName = "memory drive keeps the walk")]
    [DataRow("ssh://host/home/u/tree/", true, true, ContainerType.SshFileSystem, false, DisplayName = "remote drive keeps the walk")]
    public void WholeFolderToTrash_OnlyForALocalRecursiveFolderToTheBin(string path, bool recursive, bool moveToRecycleBin, ContainerType container, bool expected)
    {
        var url = new Url(path);
        Assert.AreEqual(container, url.ContainerType, "the test case names the container it means");

        Assert.AreEqual(expected, DeleteCommand.WholeFolderToTrash(url, recursive, moveToRecycleBin));
    }
}

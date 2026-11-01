using iConsole.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Model.ContentModels.Test;

/// <summary>The in-memory drive's tree: what is kept, what is refused, and that a move or copy re-bases what it carries.</summary>
[TestClass]
public class MemoryFolderTest
{
    private static MemoryFolder Root() => new(new Url(Container.MEMORY_DRIVE));

    private static MemoryFolder Sub(MemoryFolder parent, string name)
    {
        var folder = new MemoryFolder(new Url(Container.MEMORY_DRIVE + name + "/"));
        parent.Folders[name] = folder;
        return folder;
    }

    private static MemoryStream Bytes(string text) => new(System.Text.Encoding.UTF8.GetBytes(text));

    [Timeout(60_000)]
    [TestMethod]
    public async Task WriteThenRead_ReturnsTheFileWithItsBytes()
    {
        var root = Root();
        await root.WriteFileAsync(new Url(Container.MEMORY_DRIVE + "a.txt"), Bytes("hello"));

        var file = await root.ReadFileAsync(new Url(Container.MEMORY_DRIVE + "a.txt"));

        Assert.IsNotNull(file);
        CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes("hello"), file.Content);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadOfAMissingFile_IsNull()
    {
        Assert.IsNull(await Root().ReadFileAsync(new Url(Container.MEMORY_DRIVE + "nope.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WriteOverAnExistingFile_ReplacesItsContent()
    {
        var root = Root();
        await root.WriteFileAsync(new Url(Container.MEMORY_DRIVE + "a.txt"), Bytes("old"));
        await root.WriteFileAsync(new Url(Container.MEMORY_DRIVE + "a.txt"), Bytes("new"));

        var file = await root.ReadFileAsync(new Url(Container.MEMORY_DRIVE + "a.txt"));

        CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes("new"), file!.Content);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DeleteFile_RemovesItOnceOnly()
    {
        var root = Root();
        root.Files["a.txt"] = new MemoryFile("a.txt", Bytes("x"));

        Assert.IsTrue(root.DeleteFile(new Url(Container.MEMORY_DRIVE + "a.txt")));
        Assert.IsFalse(root.DeleteFile(new Url(Container.MEMORY_DRIVE + "a.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DeleteFolder_NonEmpty_IsRefusedWithoutRecursive_AndRemovedWith()
    {
        var root = Root();
        var docs = Sub(root, "docs");
        docs.Files["x.txt"] = new MemoryFile("x.txt", Bytes("x"));

        Assert.IsFalse(root.DeleteFolder(new Url(Container.MEMORY_DRIVE + "docs/"), recursive: false), "a non-empty folder stays unless recursive");
        Assert.IsTrue(root.Folders.ContainsKey("docs"));

        Assert.IsTrue(root.DeleteFolder(new Url(Container.MEMORY_DRIVE + "docs/"), recursive: true));
        Assert.IsFalse(root.Folders.ContainsKey("docs"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DeleteFolder_Missing_IsFalse()
    {
        Assert.IsFalse(Root().DeleteFolder(new Url(Container.MEMORY_DRIVE + "ghost/"), recursive: true));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Navigate_FollowsNestedSegments_EmptyIsThisFolder_MissingIsNull()
    {
        var root = Root();
        var b = Sub(Sub(root, "a"), "b");

        Assert.AreSame(root, root.Navigate(string.Empty));
        Assert.AreSame(b, root.Navigate("a/b"));
        Assert.IsNull(root.Navigate("a/missing"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MoveFile_LeavesTheSourceAndCarriesTheFileToTheTarget()
    {
        var root = Root();
        var dst = Sub(root, "dst");
        root.Files["a.txt"] = new MemoryFile("a.txt", Bytes("x"));

        root.MoveEntryTo(new Url(Container.MEMORY_DRIVE + "a.txt"), new Url(Container.MEMORY_DRIVE + "dst/a.txt"), dst, overwrite: false);

        Assert.IsFalse(root.Files.ContainsKey("a.txt"));
        Assert.IsTrue(dst.Files.ContainsKey("a.txt"));
        Assert.AreEqual(Container.MEMORY_DRIVE + "dst/a.txt", dst.Files["a.txt"].Url.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MoveFileOntoAnExistingFile_WithoutOverwrite_IsRefused_AndTheSourceKeepsIt()
    {
        var root = Root();
        var dst = Sub(root, "dst");
        root.Files["a.txt"] = new MemoryFile("a.txt", Bytes("source"));
        dst.Files["a.txt"] = new MemoryFile("a.txt", Bytes("target"));

        Assert.Throws<IOException>(() =>
            root.MoveEntryTo(new Url(Container.MEMORY_DRIVE + "a.txt"), new Url(Container.MEMORY_DRIVE + "dst/a.txt"), dst, overwrite: false));

        CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes("source"), root.Files["a.txt"].Content, "the refused move puts the source back");
        CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes("target"), dst.Files["a.txt"].Content);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MoveFolder_RebasesEverythingItCarries()
    {
        var root = Root();
        var docs = Sub(root, "docs");
        docs.Files["x.txt"] = new MemoryFile("x.txt", Bytes("x"));

        root.MoveEntryTo(new Url(Container.MEMORY_DRIVE + "docs/"), new Url(Container.MEMORY_DRIVE + "archive/"), root, overwrite: false);

        Assert.IsFalse(root.Folders.ContainsKey("docs"));
        var archive = root.Folders["archive"];
        Assert.AreEqual(Container.MEMORY_DRIVE + "archive/x.txt", archive.Files["x.txt"].Url.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MoveFolderIntoItsOwnSubfolder_IsRefused_AndNothingIsLost()
    {
        var root = Root();
        var docs = Sub(root, "docs");
        var inner = Sub(docs, "inner");
        docs.Files["x.txt"] = new MemoryFile("x.txt", Bytes("x"));

        Assert.Throws<IOException>(() =>
            root.MoveEntryTo(new Url(Container.MEMORY_DRIVE + "docs/"), new Url(Container.MEMORY_DRIVE + "docs/inner/moved/"), inner, overwrite: false));

        Assert.IsTrue(root.Folders.ContainsKey("docs"), "the folder is still where it was");
        Assert.IsTrue(docs.Files.ContainsKey("x.txt"), "its contents are still there");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CopyFolder_DeepClonesTheSubtree_AndLeavesTheSourceAlone()
    {
        var root = Root();
        var docs = Sub(root, "docs");
        var nested = Sub(docs, "nested");
        nested.Files["n.txt"] = new MemoryFile("n.txt", Bytes("deep"));

        root.Copy(new Url(Container.MEMORY_DRIVE + "docs/"), new Url(Container.MEMORY_DRIVE + "copy/"), root, overwrite: false);

        Assert.IsTrue(root.Folders.ContainsKey("docs"), "the source stays");
        var copiedNested = root.Folders["copy"].Folders["nested"];
        Assert.AreNotSame(nested, copiedNested, "the copy is a clone, not the same folder");
        CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes("deep"), copiedNested.Files["n.txt"].Content);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CopyFileOntoAnExistingFile_WithoutOverwrite_IsRefused()
    {
        var root = Root();
        root.Files["a.txt"] = new MemoryFile("a.txt", Bytes("source"));
        root.Files["b.txt"] = new MemoryFile("b.txt", Bytes("target"));

        Assert.Throws<IOException>(() =>
            root.Copy(new Url(Container.MEMORY_DRIVE + "a.txt"), new Url(Container.MEMORY_DRIVE + "b.txt"), root, overwrite: false));

        CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes("target"), root.Files["b.txt"].Content);
    }
}

using iConsole.Helper;
using iTerminal.Core.Services.Links;

namespace iTerminal.Core.Test.Services.Links;

/// <summary>Changing where an existing link leads — symbolic or hard — on a real disk.</summary>
[TestClass]
public class LinkEditTest
{
    private string _root = null!;

    [TestInitialize]
    public void CreateSandbox() => _root = Directory.CreateTempSubdirectory("iTerminal.LinkEdit.").FullName;

    [TestCleanup]
    public void DeleteSandbox()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string PathOf(params string[] parts) => Path.Combine([_root, .. parts]);

    private string CreateFile(string name, string content = "content")
    {
        var path = PathOf(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private bool TryCreateFileSymlink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [TestMethod]
    public async Task Symbolic_CanBePointedAtAnotherFile()
    {
        var first = CreateFile("first.txt", "one");
        var second = CreateFile("second.txt", "two");
        var link = PathOf("link.txt");
        if (!TryCreateFileSymlink(link, first))
        {
            Assert.Inconclusive("this account may not create symbolic links");
        }

        await new LinkService().ReplaceAsync(LinkKind.Symbolic, link, second);

        Assert.AreEqual("two", File.ReadAllText(link));
        Assert.AreEqual("one", File.ReadAllText(first), "the old target is untouched");
        Assert.AreEqual(0, Directory.GetFiles(_root, ".*.tmp").Length, "no temporary name left behind");
    }

    [TestMethod]
    public async Task Symbolic_RelativeTargetIsWrittenRelative()
    {
        var first = CreateFile("a/first.txt");
        var second = CreateFile("a/second.txt");
        var link = PathOf("a", "link.txt");
        if (!TryCreateFileSymlink(link, first))
        {
            Assert.Inconclusive("this account may not create symbolic links");
        }

        await new LinkService().ReplaceAsync(LinkKind.Symbolic, link, second, relative: true);

        Assert.AreEqual("second.txt", FileLinkHelper.GetLinkTarget(link));
    }

    [TestMethod]
    public async Task FolderLink_CanBePointedAtAnotherFolder()
    {
        var one = Directory.CreateDirectory(PathOf("one")).FullName;
        var two = Directory.CreateDirectory(PathOf("two")).FullName;
        File.WriteAllText(Path.Combine(two, "marker.txt"), "x");
        var link = PathOf("link");
        LinkServiceTest.CreateFolderLink(link, one);

        if (!OperatingSystem.IsWindows() || FileLinkHelper.IsLink(link))
        {
            await new LinkService().ReplaceAsync(LinkKind.Symbolic, link, two);
        }

        Assert.IsTrue(File.Exists(Path.Combine(link, "marker.txt")));
        Assert.IsTrue(Directory.Exists(one), "the old target folder is untouched");
    }

    [TestMethod]
    public async Task Replace_ToAMissingTarget_LeavesTheLinkAsItWas()
    {
        var first = CreateFile("first.txt", "one");
        var link = PathOf("link.txt");
        if (!TryCreateFileSymlink(link, first))
        {
            Assert.Inconclusive("this account may not create symbolic links");
        }

        var exception = await Assert.ThrowsExactlyAsync<IOException>(() =>
            new LinkService().ReplaceAsync(LinkKind.Symbolic, link, PathOf("missing.txt")));

        StringAssert.Contains(exception.Message, "does not exist");
        Assert.AreEqual("one", File.ReadAllText(link));
    }

    [TestMethod]
    public async Task Replace_ALinkToItself_IsRefused()
    {
        var first = CreateFile("first.txt");
        var link = PathOf("link.txt");
        if (!TryCreateFileSymlink(link, first))
        {
            Assert.Inconclusive("this account may not create symbolic links");
        }

        await Assert.ThrowsExactlyAsync<IOException>(() => new LinkService().ReplaceAsync(LinkKind.Symbolic, link, link));
    }

    [TestMethod]
    public async Task HardLink_NameCanBePointedAtAnotherFile()
    {
        var file = CreateFile("a.txt", "one");
        var other = CreateFile("c.txt", "three");
        var name = PathOf("b.txt");
        var service = new LinkService();
        await service.CreateAsync(LinkKind.Hard, name, file);

        var info = await service.InspectAsync(name);
        if (!info.IsHard)
        {
            Assert.Inconclusive("the disk would not report the number of names on this platform");
        }

        await service.ReplaceAsync(LinkKind.Hard, name, other);

        Assert.AreEqual("three", File.ReadAllText(name));
        Assert.AreEqual("one", File.ReadAllText(file), "the file's other name keeps its data");
        Assert.AreEqual("three", File.ReadAllText(other));
    }

    [TestMethod]
    public async Task OrdinaryFile_IsNotEditable_SoItsDataIsNotLost()
    {
        var file = CreateFile("a.txt", "only copy");
        var other = CreateFile("b.txt", "other");

        var info = await new LinkService().InspectAsync(file);
        Assert.IsFalse(info.IsEditable);

        await Assert.ThrowsExactlyAsync<IOException>(() => new LinkService().ReplaceAsync(LinkKind.Hard, file, other));
        Assert.AreEqual("only copy", File.ReadAllText(file));
    }

    [TestMethod]
    public async Task Inspect_ReadsASymbolicTargetAsWritten()
    {
        var first = CreateFile("a/first.txt");
        var link = PathOf("a", "link.txt");
        try
        {
            File.CreateSymbolicLink(link, "first.txt");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Inconclusive("this account may not create symbolic links");
        }

        var info = await new LinkService().InspectAsync(link);

        Assert.IsTrue(info.IsSymbolic);
        Assert.AreEqual("first.txt", info.SymbolicTarget);
        Assert.IsFalse(info.IsFolder);
        Assert.IsTrue(File.Exists(first));
    }
}

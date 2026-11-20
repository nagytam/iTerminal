using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class DirectoryListerTest
{
    private string _folder = null!;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "iTerminal.DirLister." + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_folder, "sub"));
        File.WriteAllText(Path.Combine(_folder, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_folder, ".hidden"), "h");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_folder, recursive: true);

    [TestMethod]
    public void ListsFoldersAndFiles_HiddenOnesIncluded()
    {
        var children = new DirectoryLister().List(_folder, 100).OrderBy(c => c.Name, StringComparer.Ordinal).ToList();

        CollectionAssert.AreEqual(new[] { ".hidden", "a.txt", "sub" }, children.Select(c => c.Name).ToList());
        Assert.IsTrue(children.Single(c => c.Name == "sub").IsFolder);
        Assert.IsFalse(children.Single(c => c.Name == "a.txt").IsFolder);
    }

    [TestMethod]
    public void StopsAtTheMaximum() => Assert.AreEqual(2, new DirectoryLister().List(_folder, 2).Count);

    [TestMethod]
    public void AMissingFolder_ABlankOne_AndAFile_ListAsEmpty()
    {
        var lister = new DirectoryLister();

        Assert.AreEqual(0, lister.List(Path.Combine(_folder, "nope"), 10).Count);
        Assert.AreEqual(0, lister.List(string.Empty, 10).Count);
        Assert.AreEqual(0, lister.List(Path.Combine(_folder, "a.txt"), 10).Count, "a file is not a folder");
    }
}

using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class FileReplacementTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "file-replacement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string In(string name) => Path.Combine(_folder, name);

    private string[] FileNames() => Directory.GetFiles(_folder).Select(Path.GetFileName).Select(n => n!).Order().ToArray();

    [TestMethod]
    public void ReplaceWritesTheNewBytesOverAnExistingFile()
    {
        File.WriteAllBytes(In("settings.json"), "old"u8.ToArray());

        FileReplacement.Replace(In("settings.json"), "new content"u8.ToArray());

        CollectionAssert.AreEqual("new content"u8.ToArray(), File.ReadAllBytes(In("settings.json")));
    }

    [TestMethod]
    public void ReplaceCreatesTheFileWhenThereIsNoneYet()
    {
        FileReplacement.Replace(In("fresh.txt"), [1, 2, 3]);

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(In("fresh.txt")));
    }

    [TestMethod]
    public void ReplaceWithEmptyContentEmptiesTheFile()
    {
        File.WriteAllBytes(In("log.txt"), "something"u8.ToArray());

        FileReplacement.Replace(In("log.txt"), []);

        Assert.AreEqual(0, new FileInfo(In("log.txt")).Length);
    }

    [TestMethod]
    public void ReplaceLeavesNoTemporaryFileBehind()
    {
        File.WriteAllBytes(In("data.bin"), new byte[] { 9 });

        FileReplacement.Replace(In("data.bin"), new byte[] { 7, 7 });

        CollectionAssert.AreEqual(new[] { "data.bin" }, FileNames());
    }

    [TestMethod]
    public void AFailedWriteLeavesTheOriginalAsItWas()
    {
        File.WriteAllBytes(In("keep.txt"), "original"u8.ToArray());
        // The temporary file would sit in a folder that does not exist, so the write fails before anything is moved.
        string missingFolderTarget = Path.Combine(_folder, "missing", "keep.txt");

        Assert.ThrowsExactly<DirectoryNotFoundException>(() => FileReplacement.Replace(missingFolderTarget, "x"u8.ToArray()));

        CollectionAssert.AreEqual("original"u8.ToArray(), File.ReadAllBytes(In("keep.txt")));
        CollectionAssert.AreEqual(new[] { "keep.txt" }, FileNames());
    }
}

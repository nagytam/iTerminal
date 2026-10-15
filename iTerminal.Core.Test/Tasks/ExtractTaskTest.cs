using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Tasks;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Tasks;

/// <summary>Shift+F5's task: several archives, a folder each, a failure that does not stop the rest, and drives other than the local disk.</summary>
[TestClass]
public class ExtractTaskTest
{
    private string _root = string.Empty;
    private readonly FileSystemService _fileSystem = new();
    private readonly MemoryDriveService _memory = new();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _root = Path.Combine(Path.GetTempPath(), "ExtractTaskTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        Directory.CreateDirectory(Path.Combine(_root, "dst"));
        File.WriteAllText(Path.Combine(_root, "src", "a.txt"), "alpha");
        await Pack("one.zip", new CompressionOptions(ArchiveFormat.Zip, 6));
        await Pack("two.tar.gz", new CompressionOptions(ArchiveFormat.TarGz, 6));
        await Pack("locked.zip", new CompressionOptions(ArchiveFormat.Zip, 6, "s3cret"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private Task Pack(string name, CompressionOptions options) =>
        ManagedArchiveWriter.WriteAsync(Path.Combine(_root, name),
            [new ArchiveItem("a.txt", false, 5, DateTime.Now, Path.Combine(_root, "src", "a.txt"))], options, null, CancellationToken.None);

    private IFileSystem Resolve(Url url) => url.ContainerType == ContainerType.MemoryDrive ? _memory : _fileSystem;

    private static ArchiveExtractionService Service() => new(new FakeProcessRunner(), () => null);

    private Url Archive(string name) => new(Path.Combine(_root, name));

    private Url Destination => new(Path.Combine(_root, "dst") + Path.DirectorySeparatorChar);

    [TestMethod]
    public async Task EachArchiveGoesIntoAFolderNamedAfterIt()
    {
        var task = new ExtractTask(Service(), [Archive("one.zip"), Archive("two.tar.gz")], Resolve, Destination, new ExtractionOptions(), eachIntoOwnFolder: true);

        await task.RunAsync();

        Assert.IsNull(task.Error, task.Description);
        Assert.AreEqual("alpha", File.ReadAllText(Path.Combine(_root, "dst", "one", "a.txt")));
        Assert.AreEqual("alpha", File.ReadAllText(Path.Combine(_root, "dst", "two", "a.txt")));
        StringAssert.Contains(task.Description, "Extracted 2 file(s) from 2 of 2 archive(s)");
    }

    [TestMethod]
    public async Task IntoOneFolderTheSecondCopyIsLeftAlone()
    {
        var task = new ExtractTask(Service(), [Archive("one.zip"), Archive("two.tar.gz")], Resolve, Destination, new ExtractionOptions(), eachIntoOwnFolder: false);

        await task.RunAsync();

        Assert.AreEqual(new ExtractionResult(1, 1), task.Result);
        StringAssert.Contains(task.Description, "1 file(s) already there were left alone");
    }

    [TestMethod]
    public async Task ALockedArchiveFailsAndTheOthersStillGo()
    {
        var task = new ExtractTask(Service(), [Archive("locked.zip"), Archive("one.zip")], Resolve, Destination, new ExtractionOptions(), eachIntoOwnFolder: true);

        await task.RunAsync();

        Assert.AreEqual("locked.zip: locked.zip is encrypted: type its password.", task.Error);
        Assert.IsTrue(File.Exists(Path.Combine(_root, "dst", "one", "a.txt")));
        Assert.AreEqual(iConsole.Data.TaskStatus.Completed, task.Status);
    }

    [TestMethod]
    public async Task AnArchiveOnTheMemoryDriveUnpacksIntoAFolderOnTheMemoryDrive()
    {
        await using (var zip = File.OpenRead(Path.Combine(_root, "one.zip")))
        {
            await _memory.StreamFromAsync(new Url("memdrive://one.zip"), zip, null, CancellationToken.None);
        }

        var task = new ExtractTask(Service(), [new Url("memdrive://one.zip")], Resolve, new Url("memdrive://"), new ExtractionOptions(), eachIntoOwnFolder: true);
        await task.RunAsync();

        Assert.IsNull(task.Error, task.Description);
        var file = await _memory.ReadFileAsync(new Url("memdrive://one/a.txt"));
        Assert.AreEqual("alpha", System.Text.Encoding.UTF8.GetString(file!.Content!));
    }

    [TestMethod]
    public void TheDestinationIsAFolderInsideTheTarget()
    {
        Assert.AreEqual("memdrive://x/one/", ExtractTask.DestinationFor(new Url("memdrive://x/"), "one", true).FullUrl);
        Assert.AreEqual("memdrive://x/", ExtractTask.DestinationFor(new Url("memdrive://x/"), "one", false).FullUrl);
    }
}

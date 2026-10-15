using System.IO.Compression;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Tasks;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Tasks;

/// <summary>Alt+F5's task: the sources walked into entries, and an archive on a drive that is not the local disk.</summary>
[TestClass]
public class CompressTaskTest
{
    private string _root = string.Empty;
    private readonly FileSystemService _fileSystem = new();
    private readonly MemoryDriveService _memory = new();

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "CompressTaskTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_root, "photos", "2024"));
        File.WriteAllText(Path.Combine(_root, "photos", "2024", "a.jpg"), "jpeg");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "notes");
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

    private IFileSystem Resolve(Url url) => url.ContainerType == ContainerType.MemoryDrive ? _memory : _fileSystem;

    private static IArchiveCompressionService Service() =>
        new ArchiveCompressionService(new FakeProcessRunner(), new SevenZipArchiveWriter(new FakeProcessRunner(), () => null));

    private Url Folder(string name) => new(Path.Combine(_root, name) + Path.DirectorySeparatorChar);

    private Url FileUrl(string name) => new(Path.Combine(_root, name));

    [TestMethod]
    public void EachSourceIsNamedAfterItselfWithItsContentsBelow()
    {
        var items = CompressTask.CollectItems([Folder("photos"), FileUrl("notes.txt")], Resolve, FileUrl("out.zip"), CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "photos/", "photos/2024/", "photos/2024/a.jpg", "notes.txt" },
            items.Select(i => i.EntryName).ToArray());
        Assert.AreEqual(4, items.Single(i => i.EntryName == "photos/2024/a.jpg").Size);
    }

    [TestMethod]
    public void TheArchiveIsNotPutInsideItself()
    {
        File.WriteAllText(Path.Combine(_root, "photos", "pack.zip"), "an older archive");

        var items = CompressTask.CollectItems([Folder("photos")], Resolve, new Url(Path.Combine(_root, "photos", "pack.zip")), CancellationToken.None);

        Assert.IsFalse(items.Any(i => i.EntryName.EndsWith("pack.zip")));
    }

    [TestMethod]
    public async Task AnArchiveOnTheLocalDiskIsWritten()
    {
        var archive = FileUrl("out.zip");
        var task = new CompressTask(Service(), [Folder("photos"), FileUrl("notes.txt")], Resolve, archive, new CompressionOptions(ArchiveFormat.Zip, 6));

        await task.RunAsync();

        Assert.IsNull(task.Error, task.Description);
        using var zip = ZipFile.OpenRead(archive.FullName);
        CollectionAssert.IsSubsetOf(new[] { "photos/2024/a.jpg", "notes.txt" }, zip.Entries.Select(e => e.FullName).ToArray());
        StringAssert.Contains(task.Description, "Compressed 2 file(s) into out.zip");
        Assert.AreEqual(100, task.ProgressPercentage);
    }

    [TestMethod]
    public async Task AnArchiveOnTheMemoryDriveIsBuiltLocallyAndUploadedAndAddedTo()
    {
        var archive = new Url("memdrive://pack.zip");
        await new CompressTask(Service(), [FileUrl("notes.txt")], Resolve, archive, new CompressionOptions(ArchiveFormat.Zip, 6)).RunAsync();

        var second = new CompressTask(Service(), [Folder("photos")], Resolve, archive, new CompressionOptions(ArchiveFormat.Zip, 6));
        await second.RunAsync();

        Assert.IsNull(second.Error, second.Description);
        var file = await _memory.ReadFileAsync(archive);
        using var zip = new ZipArchive(new MemoryStream(file!.Content!));
        CollectionAssert.IsSubsetOf(new[] { "notes.txt", "photos/2024/a.jpg" }, zip.Entries.Select(e => e.FullName).ToArray(),
            "the second run fetched the archive and added to it");
    }

    [TestMethod]
    public async Task AFailureIsReportedAndKeepsTheWindowUp()
    {
        var task = new CompressTask(Service(), [FileUrl("notes.txt")], Resolve, FileUrl("out.tar"),
            new CompressionOptions(ArchiveFormat.Tar, 0, Password: "not for tar"));

        await task.RunAsync();

        StringAssert.Contains(task.Error, "cannot be password-protected");
        Assert.IsFalse(File.Exists(Path.Combine(_root, "out.tar")));
    }
}

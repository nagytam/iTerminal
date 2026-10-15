using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Preview;
using System.Text;

namespace iTerminal.Core.Test.Services.Preview;

/// <summary>What the quick-view panel shows for each kind of entry.</summary>
[TestClass]
public class FilePreviewServiceTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateTree()
    {
        _root = Path.Combine(Path.GetTempPath(), "preview_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "folder", "inner"));
        File.WriteAllText(Path.Combine(_root, "folder", "a.txt"), "12345");
    }

    [TestCleanup]
    public void DeleteTree()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    private static IEntry Entry(string path) => new FileSystemEntry(path);

    private static Task<FilePreview> BuildAsync(IEntry? entry, IFileSystem? fileSystem = null, int width = 60) =>
        new FilePreviewService().BuildAsync(entry, fileSystem, width, CancellationToken.None);

    [TestMethod]
    public async Task ATextFileShowsItsLinesWithTabsExpanded()
    {
        var path = Path.Combine(_root, "code.cs");
        File.WriteAllText(path, "class A\n{\n\tint x;\n}\n");

        var preview = await BuildAsync(Entry(path));

        Assert.AreEqual("code.cs", preview.Title);
        CollectionAssert.AreEqual(new[] { "class A", "{", "    int x;", "}", string.Empty }, preview.Lines.ToArray());
        StringAssert.Contains(preview.Summary, "UTF-8");
    }

    [TestMethod]
    public async Task ALongLineIsCutToTheWidthNotWrapped()
    {
        var path = Path.Combine(_root, "wide.txt");
        File.WriteAllText(path, new string('x', 100));

        var preview = await BuildAsync(Entry(path), width: 20);

        Assert.AreEqual(20, preview.Lines[0].Length);
        StringAssert.EndsWith(preview.Lines[0], "…");
    }

    [TestMethod]
    public async Task ALargeFileIsPreviewedFromItsHeadOnly()
    {
        var path = Path.Combine(_root, "big.log");
        File.WriteAllText(path, string.Concat(Enumerable.Repeat("a line of log text\n", 20_000)));

        var preview = await BuildAsync(Entry(path));

        Assert.IsTrue(preview.Lines.Count <= FilePreviewService.MaxLines + 2);
        StringAssert.Contains(preview.Lines[^1], "F3 shows the whole file");
        StringAssert.Contains(preview.Summary, "first part");
    }

    [TestMethod]
    public async Task ABinaryFileIsAHexDump()
    {
        var path = Path.Combine(_root, "data.bin");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0x00, 0x01, 0x41, 0x42]);

        var preview = await BuildAsync(Entry(path));

        StringAssert.StartsWith(preview.Lines[0], "00000000  4D 5A 00 01 41 42");
        StringAssert.EndsWith(preview.Lines[0], "MZ..AB");
        StringAssert.Contains(preview.Summary, "binary");
    }

    [TestMethod]
    public async Task InvalidUtf8IsReadAsLatin1()
    {
        var path = Path.Combine(_root, "latin.txt");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes("Grüße"));

        var preview = await BuildAsync(Entry(path));

        Assert.AreEqual("Grüße", preview.Lines[0]);
        StringAssert.Contains(preview.Summary, "Latin-1");
    }

    [TestMethod]
    public async Task AFolderIsSummarisedAndListed()
    {
        var preview = await BuildAsync(Entry(Path.Combine(_root, "folder") + Path.DirectorySeparatorChar));

        Assert.AreEqual("1 folder, 1 file", preview.Summary);
        Assert.IsTrue(preview.Lines.Any(l => l.TrimStart().StartsWith("1 folder, 1 file, ")), string.Join("|", preview.Lines));
        Assert.IsTrue(preview.Lines.Any(l => l.TrimStart().StartsWith("inner" + Path.DirectorySeparatorChar)));
        Assert.IsTrue(preview.Lines.Any(l => l.TrimStart().StartsWith("a.txt")));
    }

    [TestMethod]
    public async Task TheWayUpRowHasNothingToPreview()
    {
        var preview = await BuildAsync(new FileSystemEntry("..", _root));

        StringAssert.Contains(string.Join(" ", preview.Lines), "The way up");
    }

    [TestMethod]
    public async Task AFileOnAnotherDriveIsReadThroughThatDrive()
    {
        var drive = new StreamingFileSystem("remote text\nsecond line\n");
        var entry = new VirtualDriveEntry(new Url("docker://web/etc/motd"), "motd", "23 B") { Size = 23 };

        var preview = await BuildAsync(entry, drive);

        CollectionAssert.AreEqual(new[] { "remote text", "second line", string.Empty }, preview.Lines.ToArray());
    }

    [TestMethod]
    public async Task AHugeRemoteFileIsNotReadPastTheHead()
    {
        var drive = new StreamingFileSystem(new string('z', 10 * FilePreviewService.MaxTextBytes));
        var entry = new VirtualDriveEntry(new Url("docker://web/var/log/huge.log"), "huge.log") { Size = 10 * FilePreviewService.MaxTextBytes };

        var preview = await BuildAsync(entry, drive);

        Assert.IsTrue(drive.BytesWritten <= FilePreviewService.MaxTextBytes + 64 * 1024, $"wrote {drive.BytesWritten}");
        StringAssert.Contains(preview.Summary, "first part");
    }

    [TestMethod]
    public async Task AnUnreadableFileSaysWhyInsteadOfThrowing()
    {
        var entry = new VirtualDriveEntry(new Url("docker://web/secret"), "secret");

        var preview = await BuildAsync(entry, new StreamingFileSystem(null));

        StringAssert.Contains(string.Join(" ", preview.Lines), "Cannot preview: permission denied");
    }

    /// <summary>A drive that streams fixed content in 4 KB chunks, or fails.</summary>
    private sealed class StreamingFileSystem(string? content) : IFileSystem
    {
        public long BytesWritten { get; private set; }

        public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            if (content is null)
            {
                throw new IOException("permission denied");
            }

            var bytes = Encoding.UTF8.GetBytes(content);
            for (var offset = 0; offset < bytes.Length; offset += 4096)
            {
                var count = Math.Min(4096, bytes.Length - offset);
                await destination.WriteAsync(bytes.AsMemory(offset, count), cancellationToken);
                BytesWritten += count;
            }
        }

        public bool IsReadOnly => true;
        public Task WriteFileAsync(Url url, MemoryStream inputStream) => throw new NotSupportedException();
        public Task<IMemoryFile?> ReadFileAsync(Url url) => throw new NotSupportedException();
        public bool DeleteFile(Url url, bool moveToRecycleBin) => throw new NotSupportedException();
        public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => throw new NotSupportedException();
        public string GetAvailableDiskSpace() => string.Empty;
        public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw new NotSupportedException();
        public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw new NotSupportedException();
        public bool ExistsFile(Url url) => content is not null;
        public bool ExistsFolder(Url url) => false;
        public Task CreateFolderAsync(Url url) => throw new NotSupportedException();
        public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

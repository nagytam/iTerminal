using iTerminal.Core.Services.Duplicates;

namespace iTerminal.Core.Test.Services.Duplicates;

/// <summary>The duplicate scan on real folders: same bytes are found, same size or same start are not mistaken for them.</summary>
[TestClass]
public class DuplicateFinderServiceTest
{
    private string _root = string.Empty;
    private static readonly DateTime Noon = new(2024, 5, 6, 12, 0, 0);

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "DuplicateFinderServiceTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
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

    private string Write(string relative, byte[] content, DateTime? time = null)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        File.SetLastWriteTime(path, time ?? Noon);
        return path;
    }

    private string Write(string relative, string content, DateTime? time = null) => Write(relative, System.Text.Encoding.UTF8.GetBytes(content), time);

    private async Task<DuplicateScan> ScanAsync()
    {
        var scan = new DuplicateFinderService().Start(_root);
        await scan.Completion;
        return scan;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task FilesWithTheSameBytesAreOneGroupOldestFirst()
    {
        Write("a/photo.jpg", "the same picture", Noon.AddDays(2));
        Write("b/copy of photo.jpg", "the same picture", Noon);
        Write("c/deep/photo again.jpg", "the same picture", Noon.AddDays(1));
        Write("other.txt", "something else entirely");

        var scan = await ScanAsync();

        var group = scan.Groups.Single();
        Assert.AreEqual(3, group.Files.Count);
        Assert.AreEqual("copy of photo.jpg", group.Files[0].Name, "the oldest copy first: it is usually the original");
        Assert.AreEqual("photo.jpg", group.Files[2].Name);
        Assert.AreEqual("the same picture".Length * 2L, group.Wasted);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SameSizeIsNotEnough()
    {
        Write("a.txt", "0123456789");
        Write("b.txt", "9876543210");

        Assert.AreEqual(0, (await ScanAsync()).Groups.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SameStartButADifferentEndIsNotADuplicate()
    {
        // Longer than the sample: only the whole-file stage can tell these apart.
        var first = new byte[DuplicateFinderService.SampleBytes * 3];
        var second = (byte[])first.Clone();
        second[^1] = 1;
        Write("a.bin", first);
        Write("b.bin", second);
        Write("c.bin", first);

        var group = (await ScanAsync()).Groups.Single();

        CollectionAssert.AreEquivalent(new[] { "a.bin", "c.bin" }, group.Files.Select(f => f.Name).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EmptyFilesAreNotDuplicates()
    {
        Write("a.txt", string.Empty);
        Write("b.txt", string.Empty);

        Assert.AreEqual(0, (await ScanAsync()).Groups.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task VersionControlInternalsAreLeftAlone()
    {
        Write("work/file.txt", "content");
        Write("work/.git/objects/copy.txt", "content");

        Assert.AreEqual(0, (await ScanAsync()).Groups.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BiggestWasteComesFirst()
    {
        Write("s1/small.txt", "ab");
        Write("s2/small.txt", "ab");
        Write("b1/big.txt", new string('x', 5000));
        Write("b2/big.txt", new string('x', 5000));

        var groups = (await ScanAsync()).Groups;

        Assert.AreEqual(2, groups.Count);
        Assert.AreEqual(5000, groups[0].Size);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CancellingEndsTheScanWithNothing()
    {
        Write("a.txt", "same");
        Write("b.txt", "same");
        var scan = new DuplicateFinderService().Start(_root);
        scan.Cancel();
        await scan.Completion;

        Assert.IsTrue(scan.IsComplete);
        Assert.IsTrue(scan.WasCancelled);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AGroupWithOneCopyLeftIsNoLongerADuplicate()
    {
        var a = new DuplicateFile("a", Noon);
        var b = new DuplicateFile("b", Noon);
        var c = new DuplicateFile("c", Noon);
        var group = new DuplicateGroup(10, [a, b, c]);

        Assert.AreEqual(2, group.Without(new HashSet<string> { "a" })!.Files.Count);
        Assert.IsNull(group.Without(new HashSet<string> { "a", "b" }));
    }
}

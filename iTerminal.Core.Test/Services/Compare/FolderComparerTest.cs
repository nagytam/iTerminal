using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Services.Compare;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.Compare;

/// <summary>Shift+F2's engine, on real folders: what is the same, what is newer where, and what differs with no winner.</summary>
[TestClass]
public class FolderComparerTest
{
    private string _root = string.Empty;
    private static readonly DateTime Noon = new(2024, 5, 6, 12, 0, 0);
    private readonly FileSystemService _fileSystem = new();

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "FolderComparerTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_root, "L"));
        Directory.CreateDirectory(Path.Combine(_root, "R"));
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

    private void Write(string side, string relative, string content, DateTime time)
    {
        var path = Path.Combine(_root, side, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, time);
    }

    private Url Folder(string side) => new(Path.Combine(_root, side) + Path.DirectorySeparatorChar);

    private Task<CompareResult> Compare(CompareMode mode = CompareMode.SizeAndDate, bool subfolders = false) =>
        FolderComparer.CompareAsync(Folder("L"), _fileSystem, Folder("R"), _fileSystem, new CompareOptions(mode, subfolders), null, CancellationToken.None);

    private static DiffKind KindOf(CompareResult result, string path) => result.Items.Single(i => i.Path == path).Kind;

    [Timeout(60_000)]
    [TestMethod]
    public async Task EachNameIsJudgedByItsSizeAndDate()
    {
        Write("L", "same.txt", "same", Noon);
        Write("R", "same.txt", "same", Noon.AddSeconds(1));      // within the 2 s two local files are allowed
        Write("L", "left.txt", "only here", Noon);
        Write("R", "right.txt", "only there", Noon);
        Write("L", "newer-left.txt", "v2", Noon.AddHours(1));
        Write("R", "newer-left.txt", "v1", Noon);
        Write("L", "newer-right.txt", "v1", Noon);
        Write("R", "newer-right.txt", "v2", Noon.AddHours(1));
        Write("L", "clash.txt", "short", Noon);
        Write("R", "clash.txt", "much longer", Noon);            // same date, different size: no winner
        Directory.CreateDirectory(Path.Combine(_root, "L", "only-left-folder"));

        var result = await Compare();

        Assert.AreEqual(DiffKind.Identical, KindOf(result, "same.txt"));
        Assert.AreEqual(DiffKind.LeftOnly, KindOf(result, "left.txt"));
        Assert.AreEqual(DiffKind.RightOnly, KindOf(result, "right.txt"));
        Assert.AreEqual(DiffKind.LeftNewer, KindOf(result, "newer-left.txt"));
        Assert.AreEqual(DiffKind.RightNewer, KindOf(result, "newer-right.txt"));
        Assert.AreEqual(DiffKind.Different, KindOf(result, "clash.txt"));
        Assert.AreEqual(DiffKind.LeftOnly, KindOf(result, "only-left-folder"));
        Assert.AreEqual("only-left-folder", result.Items[0].Path, "folders first");
        Assert.AreEqual(
            "1 only on the left, 1 only on the right, 1 newer on the left, 1 newer on the right, 1 different, 1 folder on one side only, 1 identical.",
            result.Summary());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SizeOnlyIgnoresTheDates()
    {
        Write("L", "a.txt", "same", Noon);
        Write("R", "a.txt", "same", Noon.AddDays(3));

        Assert.AreEqual(DiffKind.RightNewer, KindOf(await Compare(), "a.txt"));
        Assert.AreEqual(DiffKind.Identical, KindOf(await Compare(CompareMode.SizeOnly), "a.txt"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ContentReadsTheBytes()
    {
        Write("L", "same-bytes.txt", "abcd", Noon);
        Write("R", "same-bytes.txt", "abcd", Noon.AddDays(3));    // dates differ, bytes do not
        Write("L", "changed.txt", "abcd", Noon);
        Write("R", "changed.txt", "abce", Noon);                  // same size and date, one byte differs

        var bySizeAndDate = await Compare();
        var byContent = await Compare(CompareMode.Content);

        Assert.AreEqual(DiffKind.Identical, KindOf(bySizeAndDate, "changed.txt"), "size and date cannot see it");
        Assert.AreEqual(DiffKind.Different, KindOf(byContent, "changed.txt"));
        Assert.AreEqual(DiffKind.Identical, KindOf(byContent, "same-bytes.txt"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASubfolderStandsForWhatIsInsideIt()
    {
        Write("L", "docs/a.txt", "a", Noon);
        Write("R", "docs/a.txt", "a", Noon);
        Write("L", "docs/new.txt", "new", Noon);                  // only left inside: the folder is newer on the left
        Write("L", "mixed/x.txt", "x2", Noon.AddHours(1));
        Write("R", "mixed/x.txt", "x1", Noon);
        Write("R", "mixed/y.txt", "y", Noon);                     // one way and the other: no winner

        var flat = await Compare();
        var deep = await Compare(subfolders: true);

        Assert.AreEqual(DiffKind.Identical, KindOf(flat, "docs"), "not looked into without subfolders");
        Assert.AreEqual(DiffKind.LeftNewer, KindOf(deep, "docs"));
        Assert.AreEqual(DiffKind.LeftOnly, KindOf(deep, "docs/new.txt"));
        Assert.AreEqual(DiffKind.Different, KindOf(deep, "mixed"));
        var order = deep.Items.Select(i => i.Path).ToList();
        Assert.IsTrue(order.IndexOf("docs") < order.IndexOf("docs/a.txt"), "a folder is listed before its contents");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ALocalFolderComparesWithAZipOfItAsTheSame()
    {
        // A ZIP keeps dates to two seconds and the archive drive lists them to the minute: a fresh
        // ZIP of the folder must still come out identical, not "newer".
        Write("L", "a.txt", "alpha", Noon.AddSeconds(37));
        Write("L", "docs/b.md", "beta", Noon.AddSeconds(41));
        var zip = Path.Combine(_root, "copy.zip");
        await ManagedArchiveWriter.WriteAsync(zip,
        [
            new ArchiveItem("a.txt", false, 5, Noon.AddSeconds(37), Path.Combine(_root, "L", "a.txt")),
            new ArchiveItem("docs/", true, 0, Noon, Path.Combine(_root, "L", "docs")),
            new ArchiveItem("docs/b.md", false, 4, Noon.AddSeconds(41), Path.Combine(_root, "L", "docs", "b.md")),
        ], new CompressionOptions(ArchiveFormat.Zip, 6), null, CancellationToken.None);
        var archive = new ArchiveDriveService(new FakeProcessRunner(), () => null);

        var result = await FolderComparer.CompareAsync(Folder("L"), _fileSystem, ArchiveDriveUrl.Build(zip), archive,
            new CompareOptions(CompareMode.SizeAndDate, IncludeSubfolders: true), null, CancellationToken.None);

        Assert.IsTrue(result.Items.All(i => i.Kind == DiffKind.Identical), string.Join(", ", result.Items.Select(i => $"{i.Path}:{i.Kind}")));
        Assert.AreEqual(3, result.Items.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(new[] { DiffKind.Identical, DiffKind.Identical }, DiffKind.Identical)]
    [DataRow(new[] { DiffKind.LeftOnly, DiffKind.LeftNewer, DiffKind.Identical }, DiffKind.LeftNewer)]
    [DataRow(new[] { DiffKind.RightOnly }, DiffKind.RightNewer)]
    [DataRow(new[] { DiffKind.LeftOnly, DiffKind.RightNewer }, DiffKind.Different)]
    [DataRow(new[] { DiffKind.Different }, DiffKind.Different)]
    public void AFoldersVerdictIsTheDirectionOfEverythingInIt(DiffKind[] inside, DiffKind expected)
    {
        Assert.AreEqual(expected, FolderComparer.Aggregate(inside));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheMarksAreWhatEachSideShouldCopyAcross()
    {
        var left = MarkableTestModel.Create(Path.Combine(_root, "ml"));
        var right = MarkableTestModel.Create(Path.Combine(_root, "mr"));
        var result = new CompareResult(
        [
            new("photos", true, DiffKind.LeftNewer, null, null, null, null, null, null),
            new("photos/x.jpg", false, DiffKind.LeftOnly, null, null, null, null, null, null),
            new("a.txt", false, DiffKind.LeftOnly, null, null, null, null, null, null),
            new("b.txt", false, DiffKind.RightNewer, null, null, null, null, null, null),
            new("c.jpg", false, DiffKind.Different, null, null, null, null, null, null),
        ]);

        var marked = CompareFoldersDialog.ApplyMarks(result, left, left.Url, right, right.Url);

        Assert.AreEqual((2, 1), marked);
        CollectionAssert.AreEqual(new[] { "photos", "a.txt" }, left.MarkedNames, "only top-level rows; nested paths are inside the folder");
        CollectionAssert.AreEqual(new[] { "b.txt" }, right.MarkedNames, "c.jpg differs with no winner: marked on neither side");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APanelThatMovedOnIsNotMarked()
    {
        var left = MarkableTestModel.Create(Path.Combine(_root, "ml"));
        var right = MarkableTestModel.Create(Path.Combine(_root, "mr"));
        var compared = left.Url;
        left.SetUrl(new Url(Path.Combine(_root, "elsewhere") + Path.DirectorySeparatorChar));
        var result = new CompareResult([new("a.txt", false, DiffKind.LeftOnly, null, null, null, null, null, null)]);

        Assert.AreEqual((0, 0), CompareFoldersDialog.ApplyMarks(result, left, compared, right, right.Url));
    }
}

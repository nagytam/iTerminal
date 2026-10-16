using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Compare;
using iTerminal.Core.Tasks;
using Moq;

namespace iTerminal.Core.Test.Services.Compare;

/// <summary>Folder synchronisation on real folders: what the plan decides from a comparison, and that carrying it out leaves nothing to compare.</summary>
[TestClass]
public class FolderSynchronizerTest
{
    private string _root = string.Empty;
    private static readonly DateTime Noon = new(2024, 5, 6, 12, 0, 0);
    private readonly FileSystemService _fileSystem = new();

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "FolderSynchronizerTest_" + Guid.NewGuid().ToString("N")[..8]);
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

    private string PathOf(string side, string relative) => Path.Combine(_root, side, relative.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string side, string relative, string content, DateTime time)
    {
        var path = PathOf(side, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, time);
    }

    private Url Folder(string side) => new(Path.Combine(_root, side) + Path.DirectorySeparatorChar);

    private Task<CompareResult> Compare(IFileSystem? right = null, CompareMode mode = CompareMode.SizeAndDate) =>
        FolderComparer.CompareAsync(Folder("L"), _fileSystem, Folder("R"), right ?? _fileSystem, new CompareOptions(mode, IncludeSubfolders: true), null, CancellationToken.None);

    private void Seed()
    {
        Write("L", "same.txt", "same", Noon);
        Write("R", "same.txt", "same", Noon);
        Write("L", "left.txt", "only here", Noon);
        Write("R", "right.txt", "only there", Noon);
        Write("L", "newer-left.txt", "v2 left", Noon.AddHours(1));
        Write("R", "newer-left.txt", "v1", Noon);
        Write("L", "newer-right.txt", "v1", Noon);
        Write("R", "newer-right.txt", "v2 right", Noon.AddHours(1));
        Write("L", "clash.txt", "short", Noon);
        Write("R", "clash.txt", "much longer", Noon);
        Write("L", "dir/nested/deep.txt", "deep", Noon);
        Write("L", "dir/shared.txt", "left v2", Noon.AddHours(2));
        Write("R", "dir/shared.txt", "left v1", Noon);
    }

    private static string[] Describe(SyncPlan plan) => [.. plan.Actions.Select(a => a.Describe()).OrderBy(s => s, StringComparer.Ordinal)];

    [TestMethod]
    public async Task BothWaysCopiesWhatIsMissingAndTheNewerCopyAndLeavesConflictsAlone()
    {
        Seed();

        var plan = FolderSynchronizer.Plan(await Compare(), new SyncOptions(SyncDirection.Both));

        CollectionAssert.AreEqual(
            new[]
            {
                "copy ▶ dir/nested",            // a folder only on the left: one action for everything under it
                "copy ▶ dir/shared.txt",
                "copy ▶ left.txt",
                "copy ▶ newer-left.txt",
                "◀ copy newer-right.txt",
                "◀ copy right.txt",
            }.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
            Describe(plan));
        Assert.AreEqual(1, plan.Conflicts, "clash.txt has the same date and different bytes: no winner");
        Assert.AreEqual(0, plan.Deletes, "a two-way run never deletes");
    }

    [TestMethod]
    public async Task OneWayTakesTheSourceButKeepsATargetFileThatIsNewer()
    {
        Seed();

        var plan = FolderSynchronizer.Plan(await Compare(), new SyncOptions(SyncDirection.LeftToRight));

        Assert.IsTrue(plan.Actions.All(a => a.Kind == SyncActionKind.CopyToRight), "nothing flows back");
        CollectionAssert.DoesNotContain(Describe(plan), "◀ copy newer-right.txt");
        CollectionAssert.Contains(Describe(plan), "copy ▶ clash.txt", "an explicit source decides a pair with no newer side");
        Assert.AreEqual(1, plan.Kept, "newer-right.txt is newer on the target and is kept");
        Assert.AreEqual(0, plan.Conflicts);
    }

    [TestMethod]
    public async Task DeleteExtrasRemovesWhatOnlyTheTargetHasAndOnlyForOneWay()
    {
        Seed();
        var comparison = await Compare();

        var oneWay = FolderSynchronizer.Plan(comparison, new SyncOptions(SyncDirection.LeftToRight, DeleteExtras: true));
        var reverse = FolderSynchronizer.Plan(comparison, new SyncOptions(SyncDirection.RightToLeft, DeleteExtras: true));
        var both = FolderSynchronizer.Plan(comparison, new SyncOptions(SyncDirection.Both, DeleteExtras: true));

        CollectionAssert.Contains(Describe(oneWay), "delete right right.txt");
        Assert.AreEqual(1, oneWay.Deletes);
        CollectionAssert.Contains(Describe(reverse), "delete left left.txt");
        CollectionAssert.Contains(Describe(reverse), "delete left dir/nested");
        Assert.AreEqual(0, both.Deletes, "in a two-way run a missing file cannot say which side is wrong");
    }

    [TestMethod]
    public async Task AnIdenticalPairHasNothingToDo()
    {
        Write("L", "a.txt", "x", Noon);
        Write("R", "a.txt", "x", Noon);

        var plan = FolderSynchronizer.Plan(await Compare(), new SyncOptions());

        Assert.IsTrue(plan.IsEmpty);
        Assert.AreEqual("Nothing to synchronise.", plan.Summary());
    }

    [TestMethod]
    public async Task ASynchronisedPairComparesIdenticalAgain()
    {
        Seed();
        File.Delete(PathOf("L", "clash.txt"));
        File.Delete(PathOf("R", "clash.txt"));
        var plan = FolderSynchronizer.Plan(await Compare(), new SyncOptions());

        var task = new FolderSyncTask(plan, Folder("L"), _fileSystem, Folder("R"), _fileSystem);
        await task.RunAsync();

        Assert.IsNull(task.Error, string.Join("; ", task.Failures));
        Assert.AreEqual(plan.Actions.Count, task.Copied);
        Assert.AreEqual("deep", File.ReadAllText(PathOf("R", "dir/nested/deep.txt")));
        Assert.AreEqual("left v2", File.ReadAllText(PathOf("R", "dir/shared.txt")));
        Assert.AreEqual("v2 right", File.ReadAllText(PathOf("L", "newer-right.txt")));
        Assert.AreEqual("only there", File.ReadAllText(PathOf("L", "right.txt")));

        var again = await Compare();
        Assert.AreEqual(0, again.Differences.Count(), string.Join(", ", again.Differences.Select(d => $"{d.Path}:{d.Kind}")));
    }

    [TestMethod]
    public async Task CopyingBetweenTwoDifferentDrivesKeepsTheSourceDate()
    {
        Seed();
        File.Delete(PathOf("L", "clash.txt"));
        File.Delete(PathOf("R", "clash.txt"));
        var otherDrive = new FileSystemService();   // another instance: the task cannot use the drive's own copy and must stream
        var plan = FolderSynchronizer.Plan(await Compare(otherDrive), new SyncOptions());

        var task = new FolderSyncTask(plan, Folder("L"), _fileSystem, Folder("R"), otherDrive);
        await task.RunAsync();

        Assert.IsNull(task.Error, string.Join("; ", task.Failures));
        Assert.AreEqual("only here", File.ReadAllText(PathOf("R", "left.txt")));
        Assert.AreEqual(Noon, File.GetLastWriteTime(PathOf("R", "left.txt")), "a copy stamped 'now' would be copied straight back");
        Assert.AreEqual(0, (await Compare(otherDrive)).Differences.Count());
    }

    [TestMethod]
    public async Task DeletionsGoThroughTheDrivesRecycleOption()
    {
        // A fake: a real local delete would put the test's files in the machine's recycle bin.
        Write("L", "keep.txt", "k", Noon);
        Write("R", "keep.txt", "k", Noon);
        Write("R", "extra.txt", "x", Noon);
        Directory.CreateDirectory(PathOf("R", "extra-folder"));
        var comparison = await Compare();
        var plan = FolderSynchronizer.Plan(comparison, new SyncOptions(SyncDirection.LeftToRight, DeleteExtras: true));
        var target = new Mock<IFileSystem>();

        var task = new FolderSyncTask(plan, Folder("L"), _fileSystem, Folder("R"), target.Object);
        await task.RunAsync();

        target.Verify(f => f.DeleteFile(It.Is<Url>(u => u.FullName.EndsWith("extra.txt")), true), Times.Once);
        target.Verify(f => f.DeleteFolder(It.Is<Url>(u => u.FullName.TrimEnd('/', '\\').EndsWith("extra-folder")), true, true), Times.Once);
        Assert.AreEqual(2, task.Deleted);
    }

    [TestMethod]
    public async Task AFailedStepIsListedAndTheOthersStillRun()
    {
        Write("L", "a.txt", "a", Noon);
        Write("L", "b.txt", "b", Noon);
        var comparison = await Compare();
        var plan = FolderSynchronizer.Plan(comparison, new SyncOptions(SyncDirection.LeftToRight));
        var target = new Mock<IFileSystem>();
        target.Setup(f => f.StreamFromAsync(It.Is<Url>(u => u.FullName.EndsWith("a.txt")), It.IsAny<Stream>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk full"));

        var task = new FolderSyncTask(plan, Folder("L"), _fileSystem, Folder("R"), target.Object);
        await task.RunAsync();

        Assert.AreEqual(1, task.Copied);
        Assert.AreEqual(1, task.Failures.Count);
        StringAssert.Contains(task.Failures[0], "disk full");
        Assert.IsNotNull(task.Error, "the progress window must stay up long enough to read it");
    }

    [TestMethod]
    public void TheSummaryNamesWhatWillHappen()
    {
        var item = new DiffItem("a.txt", false, DiffKind.LeftOnly, null, null, null, null, null, null);
        var plan = new SyncPlan(
            [new SyncAction(SyncActionKind.CopyToRight, item), new SyncAction(SyncActionKind.CopyToLeft, item), new SyncAction(SyncActionKind.DeleteRight, item)],
            Conflicts: 2,
            Kept: 0);

        Assert.AreEqual("1 copy to the right, 1 copy to the left, 1 deletion. 2 conflicts left alone.", plan.Summary());
    }
}

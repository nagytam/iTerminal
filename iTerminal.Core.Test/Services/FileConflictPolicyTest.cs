using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Services;

namespace iTerminal.Core.Services.Test;

[TestClass]
public class FileConflictPolicyTest
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "conflict-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private (Url Source, Url Target) Pair(string sourceText, string targetText, DateTime? sourceTime = null, DateTime? targetTime = null)
    {
        // Each pair gets its own folder: Url caches a file's size and date for a few seconds by path.
        var folder = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "src.txt");
        var target = Path.Combine(folder, "dst.txt");
        File.WriteAllText(source, sourceText);
        File.WriteAllText(target, targetText);
        if (sourceTime is { } s)
        {
            File.SetLastWriteTime(source, s);
        }
        if (targetTime is { } t)
        {
            File.SetLastWriteTime(target, t);
        }
        return (new Url(source), new Url(target));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Overwrite_Replaces_Skip_Keeps_Rename_WritesNewName()
    {
        var (source, target) = Pair("new", "old");

        var overwrite = FileConflictPolicy.Decide(FileConflictChoice.Overwrite, source, target, _ => true);
        Assert.IsFalse(overwrite!.Skip);
        Assert.IsTrue(overwrite.Replace);

        var skip = FileConflictPolicy.Decide(FileConflictChoice.Skip, source, target, _ => true);
        Assert.IsTrue(skip!.Skip);
        Assert.IsFalse(skip.Replace);

        var rename = FileConflictPolicy.Decide(FileConflictChoice.Rename, source, target, _ => false);
        Assert.IsFalse(rename!.Skip);
        Assert.IsFalse(rename.Replace);
        StringAssert.EndsWith(rename.Target.FullUrl, "dst (2).txt");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Rename_PicksTheLowestFreeNumber()
    {
        var (_, target) = Pair("a", "b");
        var taken = new HashSet<string>(StringComparer.Ordinal) { target.FullUrl[..^4] + " (2).txt", target.FullUrl[..^4] + " (3).txt" };

        var renamed = FileConflictPolicy.RenamedTarget(target, u => taken.Contains(u.FullUrl));

        StringAssert.EndsWith(renamed.FullUrl, "dst (4).txt");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeepNewer_SourceNewer_Overwrites()
    {
        var (source, target) = Pair("new", "old", DateTime.Now.AddDays(-1).Date.AddHours(10), DateTime.Now.AddDays(-2).Date.AddHours(10));

        var decision = FileConflictPolicy.Decide(FileConflictChoice.KeepNewer, source, target, _ => true);

        Assert.IsFalse(decision!.Skip);
        Assert.IsTrue(decision.Replace);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeepNewer_TargetNewer_Skips()
    {
        var (source, target) = Pair("new", "old", DateTime.Now.AddDays(-2).Date.AddHours(10), DateTime.Now.AddDays(-1).Date.AddHours(10));

        var decision = FileConflictPolicy.Decide(FileConflictChoice.KeepNewer, source, target, _ => true);

        Assert.IsTrue(decision!.Skip);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeepNewer_EqualDates_AsksAgain()
    {
        var same = DateTime.Now.AddDays(-1).Date.AddHours(10);
        var (source, target) = Pair("new", "old", same, same);

        Assert.IsNull(FileConflictPolicy.Decide(FileConflictChoice.KeepNewer, source, target, _ => true));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeepLarger_EqualSizes_AsksAgain()
    {
        var (source, target) = Pair("same", "same");

        Assert.IsNull(FileConflictPolicy.Decide(FileConflictChoice.KeepLarger, source, target, _ => true));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeepLarger_LargerSourceOverwrites_SmallerSourceSkips()
    {
        var (source, target) = Pair("much longer text", "short");
        Assert.IsFalse(FileConflictPolicy.Decide(FileConflictChoice.KeepLarger, source, target, _ => true)!.Skip);

        var (smallSource, smallTarget) = Pair("x", "much longer text");
        Assert.IsTrue(FileConflictPolicy.Decide(FileConflictChoice.KeepLarger, smallSource, smallTarget, _ => true)!.Skip);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Metadata_IsUnknown_ForNonLocalPaths()
    {
        Assert.IsNull(FileConflictPolicy.ModifiedOf(new Url("memdrive://copy/a.txt")));
        Assert.IsNull(FileConflictPolicy.SizeOf(new Url("memdrive://copy/a.txt")));
    }
}

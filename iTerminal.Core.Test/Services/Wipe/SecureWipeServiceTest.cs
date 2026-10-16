using iTerminal.Core.Services.Wipe;
using System.Diagnostics;

namespace iTerminal.Core.Test.Services.Wipe;

[TestClass]
public class SecureWipeServiceTest
{
    private string _root = null!;

    [TestInitialize]
    public void CreateSandbox() => _root = Directory.CreateTempSubdirectory("iTerminal.Wipe.").FullName;

    [TestCleanup]
    public void DeleteSandbox()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string CreateFile(string relative, int length = 1000)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Repeat((byte)'S', length).ToArray());
        return path;
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task WipeAsync_AFile_IsGone()
    {
        var path = CreateFile("secret.txt");

        var result = await new SecureWipeService().WipeAsync([path], WipePattern.Random);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, result.Files);
        Assert.AreEqual(1000, result.Bytes);
        Assert.IsFalse(File.Exists(path));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(_root).Length, "the renamed file must not be left behind either");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task WipeAsync_AReadOnlyFile_IsWiped()
    {
        var path = CreateFile("ro.txt");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        var result = await new SecureWipeService().WipeAsync([path], WipePattern.Random);

        Assert.IsTrue(result.Success, string.Join("; ", result.Failures));
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task WipeAsync_AFolder_RemovesItAndEverythingInIt()
    {
        var folder = Path.Combine(_root, "tree");
        CreateFile("tree/a.txt");
        CreateFile("tree/sub/b.txt", 5000);
        Directory.CreateDirectory(Path.Combine(folder, "empty"));

        var result = await new SecureWipeService().WipeAsync([folder], WipePattern.ThreePass);

        Assert.IsTrue(result.Success, string.Join("; ", result.Failures));
        Assert.AreEqual(2, result.Files);
        Assert.AreEqual(3, result.Folders, "tree, sub and empty");
        Assert.AreEqual(6000, result.Bytes);
        Assert.IsFalse(Directory.Exists(folder));
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task WipeAsync_DestroysTheContentOfAnotherNameForTheSameFile()
    {
        // A hard link is a second name for the same bytes: whatever the wipe does to the file shows through it.
        var path = CreateFile("original.txt");
        var other = Path.Combine(_root, "other.txt");
        if (!TryHardLink(path, other))
        {
            Assert.Inconclusive("this machine cannot make a hard link");
        }

        var result = await new SecureWipeService().WipeAsync([path], WipePattern.Random);

        Assert.IsTrue(result.Success, string.Join("; ", result.Failures));
        Assert.IsFalse(File.Exists(path));
        Assert.AreEqual(0, new FileInfo(other).Length, "the bytes are gone from the other name too, not just unlinked from this one");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task WipeAsync_ALinkToAFolder_IsRemovedAndTheFolderItPointsAtSurvives()
    {
        var target = Path.Combine(_root, "target");
        var keep = CreateFile("target/keep.txt");
        var link = Path.Combine(_root, "link");
        if (!TryFolderLink(link, target))
        {
            Assert.Inconclusive("this machine cannot make a folder link");
        }

        var result = await new SecureWipeService().WipeAsync([link], WipePattern.Random);

        Assert.IsTrue(result.Success, string.Join("; ", result.Failures));
        Assert.IsFalse(Directory.Exists(link));
        Assert.IsTrue(File.Exists(keep), "wiping through a link would destroy what it leads to");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task WipeAsync_AFolderHoldingALink_DoesNotDescendIntoIt()
    {
        var target = Path.Combine(_root, "target");
        var keep = CreateFile("target/keep.txt");
        CreateFile("tree/a.txt");
        if (!TryFolderLink(Path.Combine(_root, "tree", "link"), target))
        {
            Assert.Inconclusive("this machine cannot make a folder link");
        }

        var result = await new SecureWipeService().WipeAsync([Path.Combine(_root, "tree")], WipePattern.Random);

        Assert.IsTrue(result.Success, string.Join("; ", result.Failures));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "tree")));
        Assert.IsTrue(File.Exists(keep));
        Assert.AreEqual(1000, new FileInfo(keep).Length);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task WipeAsync_AMissingPath_IsReportedAndTheRestGoOn()
    {
        var path = CreateFile("real.txt");

        var result = await new SecureWipeService().WipeAsync([Path.Combine(_root, "missing.txt"), path], WipePattern.Random);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(1, result.Failures.Count);
        StringAssert.Contains(result.Failures[0], "missing.txt");
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task WipeAsync_ALockedFile_IsReportedAndLeftInPlace()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("only Windows refuses to open a file another handle has open");
        }

        var path = CreateFile("locked.txt");
        using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var result = await new SecureWipeService().WipeAsync([path], WipePattern.Random);

        Assert.AreEqual(1, result.Failures.Count);
        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    [Timeout(15_000)]
    public void Measure_IsTheBytesTimesThePasses()
    {
        CreateFile("tree/a.txt", 100);
        CreateFile("tree/sub/b.txt", 50);
        var service = new SecureWipeService();

        Assert.AreEqual(150, service.Measure([Path.Combine(_root, "tree")], WipePattern.Random));
        Assert.AreEqual(450, service.Measure([Path.Combine(_root, "tree")], WipePattern.ThreePass));
        Assert.AreEqual(1050, service.Measure([Path.Combine(_root, "tree")], WipePattern.SevenPass));
    }

    [TestMethod]
    [Timeout(15_000)]
    public void PassesOf_EveryPatternHasItsPassesAndARandomOne()
    {
        Assert.AreEqual(1, SecureWipeService.PassesOf(WipePattern.Random).Count);
        Assert.IsNull(SecureWipeService.PassesOf(WipePattern.Random)[0]);
        Assert.AreEqual(3, SecureWipeService.PassesOf(WipePattern.ThreePass).Count);
        Assert.AreEqual(7, SecureWipeService.PassesOf(WipePattern.SevenPass).Count);
        Assert.IsTrue(SecureWipeService.PassesOf(WipePattern.ThreePass).Contains(null), "a random pass is part of the scheme");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task WipeAsync_ReportsEveryByteOfEveryPass()
    {
        var path = CreateFile("p.bin", 200_000);
        WipeProgress last = default;
        var progress = new Collector(p => last = p);

        await new SecureWipeService().WipeAsync([path], WipePattern.ThreePass, progress);

        Assert.AreEqual(600_000, last.BytesTotal);
        Assert.AreEqual(600_000, last.BytesDone);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task WipeAsync_Cancelled_StopsAndSaysSo()
    {
        var first = CreateFile("a.bin", 100_000);
        var second = CreateFile("b.bin", 100_000);
        using var cancellation = new CancellationTokenSource();
        var progress = new Collector(_ => cancellation.Cancel());

        var result = await new SecureWipeService().WipeAsync([first, second], WipePattern.Random, progress, cancellation.Token);

        Assert.IsTrue(result.Cancelled);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(File.Exists(second), "what was not reached is untouched");
    }

    private sealed class Collector(Action<WipeProgress> report) : IProgress<WipeProgress>
    {
        public void Report(WipeProgress value) => report(value);
    }

    private static bool TryHardLink(string existing, string link)
    {
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", $"/c mklink /H \"{link}\" \"{existing}\"")
            : new ProcessStartInfo("ln", $"\"{existing}\" \"{link}\"");
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = info.RedirectStandardError = true;
        using var process = Process.Start(info)!;
        process.WaitForExit();
        return process.ExitCode == 0 && File.Exists(link);
    }

    private static bool TryFolderLink(string link, string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // A junction needs no privilege, unlike a symbolic link.
                var info = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                {
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var process = Process.Start(info)!;
                process.WaitForExit();
                return process.ExitCode == 0 && Directory.Exists(link);
            }

            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

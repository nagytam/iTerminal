using iTerminal.Core.Services.DiskUsage;

namespace iTerminal.Core.Test.Services.DiskUsage;

[TestClass]
public class DiskUsageServiceTest
{
    private static string MakeTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "iterminal-du-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "big"));
        Directory.CreateDirectory(Path.Combine(root, "small"));
        File.WriteAllBytes(Path.Combine(root, "big", "a.bin"), new byte[5000]);
        File.WriteAllBytes(Path.Combine(root, "big", "b.bin"), new byte[3000]);
        File.WriteAllBytes(Path.Combine(root, "small", "c.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(root, "top.txt"), new byte[10]);
        return root;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Scan_TotalsEveryLevel_AndSortsLargestFirst()
    {
        var root = MakeTree();
        try
        {
            var scan = new DiskUsageService().Start(root);
            await scan.Completion;

            var tree = scan.Root!;
            Assert.AreEqual(8110, tree.Size);
            Assert.AreEqual(4, tree.FileCount);
            Assert.AreEqual(2, tree.FolderCount);
            CollectionAssert.AreEqual(new[] { "big", "small", "top.txt" }, tree.Children.Select(c => c.Name).ToArray());
            Assert.AreEqual(8000, tree.Children[0].Size);
            Assert.AreEqual("a.bin", tree.Children[0].Children[0].Name);
            Assert.AreEqual(4, scan.FilesScanned);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ManyFiles_AreFoldedIntoOneRow()
    {
        var root = Path.Combine(Path.GetTempPath(), "iterminal-du-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (var i = 0; i < DiskUsageScan.FilesKeptPerFolder + 5; i++)
            {
                File.WriteAllBytes(Path.Combine(root, $"f{i:000}.bin"), new byte[i + 1]);
            }

            var scan = new DiskUsageService().Start(root);
            await scan.Completion;

            var other = scan.Root!.Children.Single(c => c.IsOther);
            Assert.AreEqual(5, other.FileCount);
            Assert.AreEqual(DiskUsageScan.FilesKeptPerFolder + 1, scan.Root.Children.Count);
            Assert.AreEqual(scan.Root.Size, scan.Root.Children.Sum(c => c.Size));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Cancel_EndsTheScanWithoutATree()
    {
        // A walk that waits for the cancel is used, so the scan is still running when Cancel is called. Start with a
        // tiny tree could finish first, and then the cancel would have nothing to end.
        var scan = new DiskUsageScan(Path.GetTempPath());
        using var walking = new ManualResetEventSlim(false);
        scan.Run((path, parent, visit, token) =>
        {
            walking.Set();
            token.WaitHandle.WaitOne();
            token.ThrowIfCancellationRequested();
            return new DiskUsageNode(path, path, isFolder: true, parent);
        });

        Assert.IsTrue(walking.Wait(TimeSpan.FromSeconds(15)), "the walk never started");
        scan.Cancel();
        await scan.Completion;

        Assert.IsTrue(scan.IsComplete);
        Assert.IsTrue(scan.WasCancelled);
        Assert.IsNull(scan.Root);
    }
}

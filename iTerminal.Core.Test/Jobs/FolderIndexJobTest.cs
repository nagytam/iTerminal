using iConsole.Data;
using iTerminal.Core.Data;
using iTerminal.Core.Jobs;
using iTerminal.Core.Model;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class FolderIndexJobTest
{
    [TestMethod]
    public async Task UpdateCacheIfChanged_InsertsAndSkipsWhenMetadataIsUnchanged()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"iTerminal.FolderIndex.{Guid.NewGuid():N}.sqlite");

        try
        {
            TestDbContextFactory dbContextFactory = new(databasePath);
            FolderIndexJob folderIndexJob = CreateJob(dbContextFactory);
            DateTimeOffset modifiedUtc = DateTimeOffset.UtcNow;

            InvokeUpdateCacheIfChanged(folderIndexJob, "C:\\cache-test", 128, modifiedUtc);
            InvokeUpdateCacheIfChanged(folderIndexJob, "C:\\cache-test", 128, modifiedUtc);

            await using iTerminalDbContext context = await dbContextFactory.CreateDbContextAsync();
            List<FolderIndexCacheEntry> entries = await context.FolderIndexCacheEntries.ToListAsync();

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(128, entries[0].Size);
            Assert.AreEqual(modifiedUtc, entries[0].ModifiedUtc);
        }
        finally
        {
            CleanupPath(databasePath);
        }
    }

    [TestMethod]
    public async Task UpdateCacheIfChanged_UpdatesWhenSizeOrModifiedChanges()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"iTerminal.FolderIndex.{Guid.NewGuid():N}.sqlite");

        try
        {
            TestDbContextFactory dbContextFactory = new(databasePath);
            FolderIndexJob folderIndexJob = CreateJob(dbContextFactory);
            DateTimeOffset oldModifiedUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
            DateTimeOffset newModifiedUtc = DateTimeOffset.UtcNow;

            InvokeUpdateCacheIfChanged(folderIndexJob, "C:\\cache-test", 10, oldModifiedUtc);
            InvokeUpdateCacheIfChanged(folderIndexJob, "C:\\cache-test", 99, newModifiedUtc);

            await using iTerminalDbContext context = await dbContextFactory.CreateDbContextAsync();
            FolderIndexCacheEntry entry = await context.FolderIndexCacheEntries.SingleAsync(item => item.Path == "C:\\cache-test");

            Assert.AreEqual(99, entry.Size);
            Assert.AreEqual(newModifiedUtc, entry.ModifiedUtc);
        }
        finally
        {
            CleanupPath(databasePath);
        }
    }

    [TestMethod]
    public async Task Job_WalksNothingUntilTheIndexIsRequested()
    {
        string root = Path.Combine(Path.GetTempPath(), $"iTerminal.FolderIndexOnDemand.{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "child"));
        string databasePath = Path.Combine(Path.GetTempPath(), $"iTerminal.FolderIndex.{Guid.NewGuid():N}.sqlite");

        try
        {
            FolderIndexModel folderIndexModel = new();
            DriveInfoModel driveInfoModel = new();
            FolderIndexJob job = new(folderIndexModel, driveInfoModel, new TestDbContextFactory(databasePath));
            using CancellationTokenSource cancellation = new();
            Task execution = job.ExecuteAsync(cancellation.Token);

            // A ready drive is what makes a drive count as enabled; the folder under test is walked as its root.
            driveInfoModel.Drives.Add(new Drive { Name = root, Url = new Url(root), DriveInfo = new DriveInfo(Path.GetPathRoot(root)!) });
            await Task.Delay(200);

            Folder drive = (Folder)folderIndexModel.Children.Single();
            Assert.AreEqual(0, drive.Children.Count, "The walk ran before the index was asked for.");

            folderIndexModel.RequestIndexing();
            await WaitUntilAsync(() => drive.Children.Count == 1);

            cancellation.Cancel();
            await execution;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            CleanupPath(databasePath);
        }
    }

    [TestMethod]
    public void RequestIndexing_CanBeCalledAgainWithoutEffect()
    {
        FolderIndexModel folderIndexModel = new();

        folderIndexModel.RequestIndexing();
        folderIndexModel.RequestIndexing();

        Assert.IsTrue(folderIndexModel.IndexingRequested.IsCompletedSuccessfully);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "The condition was not met in time.");
            await Task.Delay(20);
        }
    }

    private static FolderIndexJob CreateJob(IDbContextFactory<iTerminalDbContext> dbContextFactory)
    {
        FolderIndexModel folderIndexModel = new();
        DriveInfoModel driveInfoModel = new();
        return new FolderIndexJob(folderIndexModel, driveInfoModel, dbContextFactory);
    }

    private static void InvokeUpdateCacheIfChanged(FolderIndexJob folderIndexJob, string path, long size, DateTimeOffset modifiedUtc)
    {
        MethodInfo? method = typeof(FolderIndexJob).GetMethod("UpdateCacheIfChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(method);
        method.Invoke(folderIndexJob, [path, size, modifiedUtc]);
    }

    private static void CleanupPath(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class TestDbContextFactory(string databasePath) : IDbContextFactory<iTerminalDbContext>
    {
        private readonly DbContextOptions<iTerminalDbContext> _options =
            new DbContextOptionsBuilder<iTerminalDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

        public iTerminalDbContext CreateDbContext()
        {
            return new iTerminalDbContext(_options);
        }

        public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateDbContext());
        }
    }
}

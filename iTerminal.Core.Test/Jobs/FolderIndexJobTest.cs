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

using iTerminal.Core.Application;
using iTerminal.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Test.Application;

[TestClass]
public class DatabaseInitializationHostedServiceTest
{
    private sealed class SqliteDbContextFactory : IDbContextFactory<iTerminalDbContext>
    {
        private readonly DbContextOptions<iTerminalDbContext> _options;

        public SqliteDbContextFactory(string databasePath)
        {
            DbContextOptionsBuilder<iTerminalDbContext> builder = new();
            builder.UseSqlite($"Data Source={databasePath}");
            _options = builder.Options;
        }

        public iTerminalDbContext CreateDbContext() => new(_options);

        public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    [TestMethod]
    public async Task StartAsync_WhenDatabaseIsCreated_StampsTheSchemaInItsOwnTable()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"DatabaseInitializationHostedServiceTest.{Guid.NewGuid():N}.sqlite");
        SqliteDbContextFactory factory = new(databasePath);

        try
        {
            DatabaseInitializationHostedService service = new(factory, new iTerminal.Core.Services.StartupWarningService());

            await service.StartAsync(CancellationToken.None);

            await using iTerminalDbContext context = await factory.CreateDbContextAsync();
            DatabaseMetadataEntry? modelHash = await context.DatabaseMetadata.FindAsync("ModelHash");
            Assert.IsNotNull(modelHash);
            Assert.IsFalse(string.IsNullOrWhiteSpace(modelHash.Value));
            Assert.IsFalse(
                context.Settings.Any(setting => setting.Key.StartsWith("__")),
                "The stamp must not be in the settings table, which another service owns and rewrites");
        }
        finally
        {
            Cleanup(databasePath);
        }
    }

    /// <summary>
    /// The regression that cost every cached row in the database. <see cref="Services.SettingsService"/>
    /// reads the settings table whole at start-up and writes its cache back later; while the schema
    /// stamp lived in that table, the stale value was read into the cache before the initializer
    /// replaced it and written back over the new one afterwards. Every subsequent start then saw a
    /// mismatch and recreated the database — so the music library, the playing queue and the folder
    /// cache were thrown away on every single run, while the settings themselves survived (they were
    /// in the cache) and hid it.
    /// </summary>
    [TestMethod]
    public async Task StartAsync_AfterTheSettingsCacheIsWrittenBack_DoesNotRecreateTheDatabase()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"DatabaseInitializationHostedServiceTest.{Guid.NewGuid():N}.sqlite");
        SqliteDbContextFactory factory = new(databasePath);

        try
        {
            DatabaseInitializationHostedService service = new(factory, new iTerminal.Core.Services.StartupWarningService());
            await service.StartAsync(CancellationToken.None);

            // A session: settings are read at start-up and flushed back by SettingsSynchronizationJob,
            // and something cached — here a music library row — is written in between.
            iTerminal.Core.Services.SettingsService settings = new(factory);
            settings.Set("LeftPanel", "musiccatalog://");

            await using (iTerminalDbContext seed = await factory.CreateDbContextAsync())
            {
                seed.MusicLibraryEntries.Add(new iTerminal.Core.Data.Music.MusicLibraryEntry
                {
                    Path = @"C:\Music\song.mp3",
                    Title = "Song",
                    Artist = "Artist",
                    Album = "Album",
                });
                await seed.SaveChangesAsync();
            }

            settings.Write();

            // The next start-up.
            DatabaseInitializationHostedService restart = new(factory, new iTerminal.Core.Services.StartupWarningService());
            await restart.StartAsync(CancellationToken.None);

            await using iTerminalDbContext context = await factory.CreateDbContextAsync();
            Assert.AreEqual(1, context.MusicLibraryEntries.Count(), "A restart must not throw the indexed library away");
            Assert.AreEqual("musiccatalog://", context.Settings.Find("LeftPanel")?.Value);
        }
        finally
        {
            Cleanup(databasePath);
        }
    }

    private static void Cleanup(string databasePath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
        catch (IOException)
        {
        }
    }

    [TestMethod]
    public async Task StopAsync_CompletesImmediately()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"DatabaseInitializationHostedServiceTest.{Guid.NewGuid():N}.sqlite");
        SqliteDbContextFactory factory = new(databasePath);

        try
        {
            DatabaseInitializationHostedService service = new(factory, new iTerminal.Core.Services.StartupWarningService());

            await service.StopAsync(CancellationToken.None);

            Assert.IsTrue(true);
        }
        finally
        {
            Cleanup(databasePath);
        }
    }
}


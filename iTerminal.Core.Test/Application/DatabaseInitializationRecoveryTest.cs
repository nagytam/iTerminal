using iTerminal.Core.Application;
using iTerminal.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Test.Application;

[TestClass]
public class DatabaseInitializationRecoveryTest
{
    private string _dbPath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"iterminal_test_{Guid.NewGuid():N}.sqlite");
    }


    // Keep the ServiceProvider alive for the duration of the test so
    // the factory singleton is not disposed mid-test.
    private ServiceProvider? _sp;

    private IDbContextFactory<iTerminalDbContext> BuildFactory()
    {
        _sp?.Dispose();
        var services = new ServiceCollection();
        services.AddDbContextFactory<iTerminalDbContext>(o =>
            o.UseSqlite($"Data Source={_dbPath}"));
        _sp = services.BuildServiceProvider();
        return _sp.GetRequiredService<IDbContextFactory<iTerminalDbContext>>();
    }

    [TestCleanup]
    public void TearDown()
    {
        _sp?.Dispose();
        // Disposing the provider returns connections to the pool but does not close them;
        // clear the pool so SQLite releases the file handle before we delete the file.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    [TestMethod]
    [Timeout(60_000)] // A hang guard: building the EF Core model can take over 10 s while the suite loads the machine.
    public async Task StartAsync_FreshDatabase_CreatesSchema()
    {
        var svc = new DatabaseInitializationHostedService(BuildFactory(), new iTerminal.Core.Services.StartupWarningService());
        await svc.StartAsync(CancellationToken.None);
        Assert.IsTrue(File.Exists(_dbPath));
        Assert.IsTrue(new FileInfo(_dbPath).Length > 1024, "Expected a valid SQLite DB (>1 KB)");
    }

    [TestMethod]
    [Timeout(60_000)] // A hang guard: building the EF Core model can take over 10 s while the suite loads the machine.
    public async Task StartAsync_CorruptDatabase_RecreatesToValidDb()
    {
        File.WriteAllText(_dbPath, "THIS IS NOT A SQLITE FILE");

        var svc = new DatabaseInitializationHostedService(BuildFactory(), new iTerminal.Core.Services.StartupWarningService());
        await svc.StartAsync(CancellationToken.None);

        Assert.IsTrue(File.Exists(_dbPath));
        Assert.IsTrue(new FileInfo(_dbPath).Length > 1024,
            $"Expected valid DB after recovery, got {new FileInfo(_dbPath).Length} bytes");

        // Verify the recovered DB is actually readable
        var factory = BuildFactory();
        await using var ctx = await factory.CreateDbContextAsync();
        var count = await ctx.Settings.CountAsync();
        Assert.IsTrue(count >= 0, "Should be able to query the recovered DB");
    }

    [TestMethod]
    [Timeout(60_000)] // A hang guard: building the EF Core model can take over 10 s while the suite loads the machine.
    public async Task StartAsync_ValidDatabase_DoesNotRecreate()
    {
        // Create a valid DB first
        var svc = new DatabaseInitializationHostedService(BuildFactory(), new iTerminal.Core.Services.StartupWarningService());
        await svc.StartAsync(CancellationToken.None);
        var sizeBefore = new FileInfo(_dbPath).Length;

        // Run again — should not recreate
        var svc2 = new DatabaseInitializationHostedService(BuildFactory(), new iTerminal.Core.Services.StartupWarningService());
        await svc2.StartAsync(CancellationToken.None);
        var sizeAfter = new FileInfo(_dbPath).Length;

        Assert.AreEqual(sizeBefore, sizeAfter, "Valid DB should not be recreated");
    }
}

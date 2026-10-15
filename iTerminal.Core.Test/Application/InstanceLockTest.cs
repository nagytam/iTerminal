using iTerminal.Core.Application;
using iTerminal.Core.Data;
using iTerminal.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Test.Application;

[TestClass]
public class InstanceLockTest
{
    private string _dbPath = string.Empty;
    private ServiceProvider? _sp;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"iterminal_lock_test_{Guid.NewGuid():N}.sqlite");
    }

    [TestCleanup]
    public void TearDown()
    {
        _sp?.Dispose();
        // Disposing the provider returns connections to the pool but does not close them;
        // clear the pool so SQLite releases the file handle before we delete the file.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm", _dbPath + ".lock" })
            if (File.Exists(f)) File.Delete(f);
    }

    private IDbContextFactory<iTerminalDbContext> BuildFactory()
    {
        _sp?.Dispose();
        var services = new ServiceCollection();
        services.AddDbContextFactory<iTerminalDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        _sp = services.BuildServiceProvider();
        return _sp.GetRequiredService<IDbContextFactory<iTerminalDbContext>>();
    }

    [TestMethod]
    [Timeout(60_000)] // A hang guard: building the EF Core model can take over 10 s while the suite loads the machine.
    public async Task StartAsync_FirstInstance_AcquiresLockWithNoWarnings()
    {
        using var warningService = new StartupWarningService();
        var svc = new DatabaseInitializationHostedService(BuildFactory(), warningService);
        await svc.StartAsync(CancellationToken.None);

        Assert.AreEqual(0, warningService.Warnings.Count, "First instance should have no warnings");
        Assert.IsTrue(File.Exists(_dbPath + ".lock"), "Lock file should be created");
    }

    [TestMethod]
    [Timeout(60_000)] // A hang guard: building the EF Core model can take over 10 s while the suite loads the machine.
    public async Task StartAsync_SecondInstance_AddsWarningWithDatabasePath()
    {
        // Simulate a first instance holding the lock file
        await using var lockStream = new FileStream(
            _dbPath + ".lock",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        using var warningService = new StartupWarningService();
        var svc = new DatabaseInitializationHostedService(BuildFactory(), warningService);
        await svc.StartAsync(CancellationToken.None);

        Assert.AreEqual(2, warningService.Warnings.Count, "Second instance should have 2 warning lines");
        Assert.IsTrue(warningService.Warnings[0].Contains("Another instance"), "First line should mention another instance");
        Assert.IsTrue(warningService.Warnings[1].Contains(_dbPath), "Second line should contain the DB path");
    }
}

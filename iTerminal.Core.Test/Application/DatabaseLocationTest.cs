using iTerminal.Core.Application;
using iTerminal.Core.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Test.Application;

[TestClass]
public class DatabaseLocationTest
{
    private string _dbPath = string.Empty;

    [TestInitialize]
    public void Setup() =>
        _dbPath = Path.Combine(Path.GetTempPath(), $"iterminal_location_test_{Guid.NewGuid():N}.sqlite");

    [TestCleanup]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        foreach (string f in new[] { _dbPath, _dbPath + ".lock", _dbPath + ".lock.pid" })
            if (File.Exists(f)) File.Delete(f);
    }

    [TestMethod]
    public void FirstInstance_UsesTheFile_AndRecordsItsProcessId()
    {
        using StartupWarningService warnings = new();
        using DatabaseLocation location = new(_dbPath, warnings);

        Assert.AreEqual($"Data Source={_dbPath}", location.ConnectionString);
        Assert.IsFalse(location.IsInMemory);
        Assert.AreEqual(0, warnings.Warnings.Count);
        Assert.AreEqual(Environment.ProcessId.ToString(), File.ReadAllText(_dbPath + ".lock.pid"));
    }

    [TestMethod]
    public void SecondInstance_GetsASharedInMemoryDatabase_AndTheExtendedWarning()
    {
        using StartupWarningService first = new();
        using DatabaseLocation firstLocation = new(_dbPath, first);
        _ = firstLocation.ConnectionString;

        using StartupWarningService second = new();
        using DatabaseLocation secondLocation = new(_dbPath, second);
        string connectionString = secondLocation.ConnectionString;

        Assert.IsTrue(secondLocation.IsInMemory);
        Assert.IsFalse(connectionString.Contains(_dbPath), "The second instance must not touch the locked file.");
        Assert.AreEqual(3, second.Warnings.Count);
        Assert.IsTrue(second.Warnings[0].Contains("Another instance"));
        Assert.IsTrue(second.Warnings[1].Contains(_dbPath));
        Assert.IsTrue(second.Warnings[2].Contains("in-memory"));

        // Two contexts see the same memory database.
        DbContextOptions<iTerminalDbContext> options = new DbContextOptionsBuilder<iTerminalDbContext>().UseSqlite(connectionString).Options;
        using (iTerminalDbContext a = new(options))
        {
            a.Database.EnsureCreated();
            a.Settings.Add(new PersistedSetting { Key = "k", Value = "v" });
            a.SaveChanges();
        }

        using iTerminalDbContext b = new(options);
        Assert.AreEqual("v", b.Settings.Single(s => s.Key == "k").Value);
        Assert.IsFalse(File.Exists(_dbPath), "No database file may be created by the locked-out instance.");
    }

    [TestMethod]
    public void StopOtherInstance_WithoutRecordedProcess_ReportsWhy()
    {
        Assert.IsNotNull(StartupWarningService.TryStopOtherInstance(_dbPath + ".lock"));
    }

    [TestMethod]
    public void StopOtherInstance_RefusesAProcessThatIsNotITerminal()
    {
        using System.Diagnostics.Process other = StartSleeper();
        try
        {
            File.WriteAllText(_dbPath + ".lock.pid", other.Id.ToString());
            string? error = StartupWarningService.TryStopOtherInstance(_dbPath + ".lock");

            Assert.IsNotNull(error);
            Assert.IsFalse(other.HasExited, "A foreign process must be left alone.");
        }
        finally
        {
            if (!other.HasExited) other.Kill();
        }
    }

    [TestMethod]
    public void StopOtherInstance_WhenProcessIsGone_RemovesStaleLockFiles()
    {
        File.WriteAllText(_dbPath + ".lock", string.Empty);
        File.WriteAllText(_dbPath + ".lock.pid", int.MaxValue.ToString());

        Assert.IsNull(StartupWarningService.TryStopOtherInstance(_dbPath + ".lock"));
        Assert.IsFalse(File.Exists(_dbPath + ".lock"));
        Assert.IsFalse(File.Exists(_dbPath + ".lock.pid"));
    }

    [TestMethod]
    public async Task Dialog_KStartsTheStop_OnceAndShowsTheOutcome()
    {
        int calls = 0;
        StartupWarningDialog dialog = new(["w"], iTerminal.Core.Test.Helper.UiTestHelper.TestTheme(), () => { calls++; return null; });

        Assert.IsTrue(dialog.StopStatus.Contains('K'));
        Task? run = dialog.StopOtherInstance();
        Assert.IsNotNull(run);
        await run;
        Assert.IsNull(dialog.StopOtherInstance(), "The stop is offered once.");
        Assert.AreEqual(1, calls);
        Assert.IsTrue(dialog.StopStatus.Contains("Restart"));
    }

    private static System.Diagnostics.Process StartSleeper()
    {
        System.Diagnostics.ProcessStartInfo info = OperatingSystem.IsWindows()
            ? new("ping", "-n 30 127.0.0.1")
            : new("sleep", "30");
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        return System.Diagnostics.Process.Start(info)!;
    }
}

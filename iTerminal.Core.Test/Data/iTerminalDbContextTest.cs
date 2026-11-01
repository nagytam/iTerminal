using iTerminal.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Data.Test;

/// <summary>A new database holds what the app stores, and a value written to it reads back the same.</summary>
[TestClass]
public class iTerminalDbContextTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void AFreshDatabase_KeepsASettingAcrossContexts()
    {
        var path = Path.Combine(Path.GetTempPath(), "iTerminalDbContextTest_" + Guid.NewGuid().ToString("N") + ".sqlite");
        var options = new DbContextOptionsBuilder<iTerminalDbContext>().UseSqlite($"Data Source={path}").Options;
        try
        {
            using (var writer = new iTerminalDbContext(options))
            {
                writer.Database.EnsureCreated();
                writer.Settings.Add(new PersistedSetting { Key = "theme", Value = "dark" });
                writer.SaveChanges();
            }

            using var reader = new iTerminalDbContext(options);
            Assert.AreEqual("dark", reader.Settings.Single(s => s.Key == "theme").Value);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }
}

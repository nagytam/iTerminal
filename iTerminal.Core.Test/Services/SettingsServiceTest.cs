using iTerminal.Core.Services;
using iTerminal.Core.Data;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class SettingsServiceTest
{
    private sealed class SqliteDbContextFactory : IDbContextFactory<iTerminalDbContext>
    {
        private readonly string _databasePath;
        private readonly DbContextOptions<iTerminalDbContext> _options;

        public SqliteDbContextFactory(string databasePath)
        {
            _databasePath = databasePath;
            DbContextOptionsBuilder<iTerminalDbContext> builder = new();
            builder.UseSqlite($"Data Source={_databasePath}");
            _options = builder.Options;
        }

        public iTerminalDbContext CreateDbContext() => new(_options);

        public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_Default_CreatesInstance()
    {
        SettingsService service = new();

        Assert.IsNotNull(service);
        Assert.IsFalse(service.IsDirty);
    }

    /// <summary>
    /// The settings table has one owner, and this service is it: it reads the table whole and writes
    /// its cache back whole, so any key it caches it also republishes. A key another component owns
    /// must therefore never enter the cache — a schema stamp that did was resurrected on every flush,
    /// and the database was recreated at every start-up as a result.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void ReservedKeys_AreNeitherCachedNorRepublished()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"SettingsServiceTest.{Guid.NewGuid():N}.sqlite");
        SqliteDbContextFactory factory = new(databasePath);

        try
        {
            using (iTerminalDbContext seed = factory.CreateDbContext())
            {
                seed.Database.EnsureCreated();
                seed.Settings.Add(new PersistedSetting { Key = "__ModelHash", Value = "stale" });
                seed.SaveChanges();
            }

            SettingsService service = new(factory);

            Assert.IsNull(service.Get("__ModelHash"), "A reserved key is not read into the cache");

            service.Set("__ModelHash", "someone else's value");
            Assert.IsNull(service.Get("__ModelHash"), "…and cannot be put there either");

            service.Set("Theme", "dark");
            service.Write();

            using iTerminalDbContext context = factory.CreateDbContext();
            Assert.IsFalse(
                context.Settings.Any(setting => setting.Key == "__ModelHash"),
                "A reserved row left by an older version is cleared rather than rewritten for ever");
            Assert.AreEqual("dark", context.Settings.Find("Theme")?.Value);
        }
        finally
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
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Write_PersistsSettingsAcrossInstances()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"SettingsServiceTest.{Guid.NewGuid():N}.sqlite");
        SqliteDbContextFactory factory = new(databasePath);

        try
        {
            SettingsService writer = new(factory);
            writer.Set("FeatureEnabled", "true");
            writer.Write();

            SettingsService reader = new(factory);

            Assert.AreEqual("true", reader.Get("FeatureEnabled"));
            Assert.AreEqual(true, reader.GetAsBool("FeatureEnabled"));
        }
        finally
        {
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
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TemporaryDbContextFactory_CreateDbContext_ReturnsSqliteContext()
    {
        Type factoryType = typeof(SettingsService).GetNestedType("TemporaryDbContextFactory", BindingFlags.NonPublic)!;
        object factory = Activator.CreateInstance(factoryType, nonPublic: true)!;
        MethodInfo createMethod = factoryType.GetMethod("CreateDbContext")!;

        using iTerminalDbContext context = (iTerminalDbContext)createMethod.Invoke(factory, null)!;

        Assert.IsNotNull(context);
        StringAssert.Contains(context.Database.ProviderName ?? string.Empty, "Sqlite");
    }
}


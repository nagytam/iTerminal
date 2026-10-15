using iTerminal.Core.Data;
using iTerminal.Core.Services.Portfolio;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Test.Services.Portfolio;

[TestClass]
public class PortfolioServiceTest
{
    private static (iTerminalDbContext Context, DbContextOptions<iTerminalDbContext> Options, string DatabasePath) CreateContext()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"PortfolioServiceTest.{Guid.NewGuid():N}.sqlite");
        var builder = new DbContextOptionsBuilder<iTerminalDbContext>();
        builder.UseSqlite($"Data Source={databasePath}");
        var context = new iTerminalDbContext(builder.Options);
        context.Database.EnsureCreated();
        return (context, builder.Options, databasePath);
    }

    private class TestDbContextFactory : IDbContextFactory<iTerminalDbContext>
    {
        private readonly DbContextOptions<iTerminalDbContext> _options;

        public TestDbContextFactory(DbContextOptions<iTerminalDbContext> options)
        {
            _options = options;
        }

        public iTerminalDbContext CreateDbContext()
        {
            return new iTerminalDbContext(_options);
        }

        public System.Threading.Tasks.ValueTask<iTerminalDbContext> CreateDbContextAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            return new System.Threading.Tasks.ValueTask<iTerminalDbContext>(new iTerminalDbContext(_options));
        }
    }

    private static void Cleanup(iTerminalDbContext context, string databasePath)
    {
        context.Dispose();
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
    public async Task AddHoldingAsync_ThenGetHoldingsAsync_ReturnsTheHolding()
    {
            var (context, options, databasePath) = CreateContext();
        try
        {
            // Create a TestDbContextFactory so the service can create its own contexts.
            var factory = new TestDbContextFactory(options);
            var service = new PortfolioService(factory);

            var added = await service.AddHoldingAsync("AAPL", "US", 10m, 150m, "USD", null, "notes");

            Assert.AreNotEqual(0, added.Id);

            var holdings = await service.GetHoldingsAsync();
            Assert.AreEqual(1, holdings.Count);
            Assert.AreEqual("AAPL", holdings[0].Symbol);
            Assert.AreEqual("US", holdings[0].Exchange);
            Assert.AreEqual(10m, holdings[0].Quantity);
            Assert.AreEqual(150m, holdings[0].CostBasisPerUnit);
            Assert.AreEqual("USD", holdings[0].Currency);
            Assert.AreEqual("notes", holdings[0].Notes);
        }
        finally
        {
            Cleanup(context, databasePath);
        }
    }

    [TestMethod]
    public async Task UpdateHoldingAsync_PersistsChanges()
    {
            var (context, options, databasePath) = CreateContext();
        try
        {
            var factory = new TestDbContextFactory(options);
            var service = new PortfolioService(factory);
            var added = await service.AddHoldingAsync("MSFT", null, 5m, 300m, "USD", null, null);

            added.Quantity = 8m;
            added.CostBasisPerUnit = 310m;
            await service.UpdateHoldingAsync(added);

            var holdings = await service.GetHoldingsAsync();
            Assert.AreEqual(1, holdings.Count);
            Assert.AreEqual(8m, holdings[0].Quantity);
            Assert.AreEqual(310m, holdings[0].CostBasisPerUnit);
        }
        finally
        {
            Cleanup(context, databasePath);
        }
    }

    [TestMethod]
    public async Task DeleteHoldingAsync_RemovesTheHolding()
    {
            var (context, options, databasePath) = CreateContext();
        try
        {
            var factory = new TestDbContextFactory(options);
            var service = new PortfolioService(factory);
            var added = await service.AddHoldingAsync("TSLA", "US", 2m, 200m, "USD", null, null);

            await service.DeleteHoldingAsync(added.Id);

            var holdings = await service.GetHoldingsAsync();
            Assert.AreEqual(0, holdings.Count);
        }
        finally
        {
            Cleanup(context, databasePath);
        }
    }
}

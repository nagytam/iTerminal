using iConsole.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Data;

/// <summary>
/// Pays Entity Framework's first-use cost on a background thread while the rest of start-up runs. The first context
/// builds the model, the internal service provider, loads SQLite and compiles the first queries, which took over a
/// second on the start-up path of the settings service. Those caches belong to the context type and the options, not to
/// the connection string, so a throw-away in-memory database warms them for the real one.
/// </summary>
public static class DatabaseWarmUp
{
    /// <summary>Starts the warm-up and returns at once. It never throws and nothing waits for it.</summary>
    public static Task Start() => Task.Run(Run);

    private static void Run()
    {
        try
        {
            DbContextOptions<iTerminalDbContext> options = new DbContextOptionsBuilder<iTerminalDbContext>()
                .UseSqlite("Data Source=:memory:")
                .Options;
            using iTerminalDbContext context = new(options);

            // An in-memory database lives as long as its connection: kept open between creating and reading.
            context.Database.OpenConnection();
            context.Database.EnsureCreated();
            _ = context.Settings.AsNoTracking().ToList();
            StartupTrace.Mark("database-warmed");
        }
        catch (Exception)
        {
            // Only a head start: the real first use does the same work, and reports any real problem.
        }
    }
}

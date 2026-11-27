using iTerminal.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.DevelopmentStudio.Test;

/// <summary>
/// A throwaway SQLite file with the app's schema, for the stores that keep their rows in the database. Each instance is its own file,
/// so a test never shares rows with another, and a second instance over the same file is how a test reads back what was saved.
/// </summary>
internal sealed class TestDatabase : IDbContextFactory<iTerminalDbContext>
{
    private readonly DbContextOptions<iTerminalDbContext> _options;

    public TestDatabase()
        : this(Path.Combine(Path.GetTempPath(), $"iTerminal.DevelopmentStudio.Test.{Guid.NewGuid():N}.sqlite"))
    {
    }

    private TestDatabase(string path)
    {
        _options = new DbContextOptionsBuilder<iTerminalDbContext>().UseSqlite($"Data Source={path}").Options;
        using iTerminalDbContext context = new(_options);
        context.Database.EnsureCreated();
    }

    public iTerminalDbContext CreateDbContext() => new(_options);

    public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());
}

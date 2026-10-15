using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Data;

/// <summary>
/// A throwaway SQLite file, used when a database-backed service is constructed without one — a test,
/// or a run where the real database could not be opened. The service then works exactly as it does
/// in the app instead of needing a second, in-memory code path that nothing else exercises.
/// </summary>
internal sealed class TemporaryDbContextFactory : IDbContextFactory<iTerminalDbContext>
{
    private readonly DbContextOptions<iTerminalDbContext> _options;

    public TemporaryDbContextFactory(string name)
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"iTerminal.{name}.{Guid.NewGuid():N}.sqlite");
        DbContextOptionsBuilder<iTerminalDbContext> builder = new();
        builder.UseSqlite($"Data Source={databasePath}");
        _options = builder.Options;
    }

    public iTerminalDbContext CreateDbContext() => new(_options);

    public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());
}

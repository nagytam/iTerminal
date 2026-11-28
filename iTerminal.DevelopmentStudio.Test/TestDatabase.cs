using iTerminal.Core.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.DevelopmentStudio.Test;

/// <summary>
/// A throwaway SQLite file with the app's schema, for the stores that keep their rows in the database. Each instance is its own file,
/// so a test never shares rows with another, and a second instance over the same file is how a test reads back what was saved.
/// </summary>
internal sealed class TestDatabase : IDbContextFactory<iTerminalDbContext>
{
    /// <summary>
    /// The schema, created once per test run. Creating the tables for every test costs more than the test itself, so each
    /// instance starts as a copy of this empty file.
    /// </summary>
    private static readonly Lazy<string> Template = new(CreateTemplate);

    private readonly DbContextOptions<iTerminalDbContext> _options;

    public TestDatabase()
        : this(Path.Combine(Path.GetTempPath(), $"iTerminal.DevelopmentStudio.Test.{Guid.NewGuid():N}.sqlite"))
    {
    }

    private TestDatabase(string path)
    {
        File.Copy(Template.Value, path);
        _options = new DbContextOptionsBuilder<iTerminalDbContext>().UseSqlite($"Data Source={path}").Options;
    }

    private static string CreateTemplate()
    {
        string path = Path.Combine(Path.GetTempPath(), $"iTerminal.DevelopmentStudio.Test.template.{Guid.NewGuid():N}.sqlite");
        var options = new DbContextOptionsBuilder<iTerminalDbContext>().UseSqlite($"Data Source={path}").Options;
        using (iTerminalDbContext context = new(options))
        {
            context.Database.EnsureCreated();
        }

        // The pooled connection would keep the file open for the copies.
        SqliteConnection.ClearAllPools();
        return path;
    }

    public iTerminalDbContext CreateDbContext() => new(_options);

    public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());
}

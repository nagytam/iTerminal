using iTerminal.Core.Data;
using iTerminal.Core.Data.Music;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// SQLite-backed <see cref="IMusicPlaylistStore"/>. The whole queue is rewritten on every save
/// rather than diffed: a queue is tens or hundreds of rows, it is only written when its version
/// actually moved, and "delete then insert in order" is the one implementation that cannot leave the
/// positions inconsistent with the list they came from.
/// </summary>
public sealed class MusicPlaylistStore : IMusicPlaylistStore
{
    private readonly IDbContextFactory<iTerminalDbContext> _dbContextFactory;
    private readonly Serilog.ILogger? _logger;

    /// <summary>
    /// The factory is optional for the same reason it is on <see cref="MusicLibraryService"/>: a
    /// test — or a run with no usable database — gets a throwaway SQLite file and the real code path
    /// rather than a second one nothing else exercises.
    /// </summary>
    public MusicPlaylistStore(
        IDbContextFactory<iTerminalDbContext>? dbContextFactory,
        Serilog.ILogger? logger = null)
    {
        _dbContextFactory = dbContextFactory ?? new TemporaryDbContextFactory("MusicPlaylist");
        _logger = logger;
    }

    public IReadOnlyList<MusicTrack> Load()
    {
        try
        {
            using iTerminalDbContext context = _dbContextFactory.CreateDbContext();
            context.Database.EnsureCreated();

            return
            [
                .. context.MusicPlaylistEntries
                    .AsNoTracking()
                    .OrderBy(entry => entry.Position)
                    .Select(entry => entry)
                    .AsEnumerable()
                    .Select(entry => entry.ToTrack())
            ];
        }
        catch (Exception exception)
        {
            _logger?.Warning(exception, "Could not read the saved music queue.");
            return [];
        }
    }

    public void Save(IEnumerable<MusicTrack> tracks)
    {
        try
        {
            List<MusicPlaylistEntry> rows = [];
            int position = 0;
            foreach (MusicTrack track in tracks ?? [])
            {
                if (track is not null)
                {
                    rows.Add(MusicPlaylistEntry.FromTrack(track, position++));
                }
            }

            using iTerminalDbContext context = _dbContextFactory.CreateDbContext();
            context.Database.EnsureCreated();

            context.MusicPlaylistEntries.ExecuteDelete();
            if (rows.Count > 0)
            {
                context.MusicPlaylistEntries.AddRange(rows);
                context.SaveChanges();
            }
        }
        catch (Exception exception)
        {
            // A queue that cannot be saved still plays for this session, which is not worth
            // interrupting anything over.
            _logger?.Warning(exception, "Could not save the music queue.");
        }
    }
}

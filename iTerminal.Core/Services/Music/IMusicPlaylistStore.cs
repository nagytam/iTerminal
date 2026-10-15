using iTerminal.Core.Data.Music;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// Where the *playing queue* lives between runs: a SQLite table, alongside the music index.
///
/// <para>The queue and a playlist file are different things and are stored differently on purpose.
/// A <c>.m3u8</c> (<see cref="IPlaylistService"/>) is a document the user made — it belongs in the
/// file system where it can be browsed, copied and opened by other players. The queue is session
/// state, no more a document than the panel's last folder is, and putting it in the database is what
/// keeps it consistent with everything else the app remembers about a session.</para>
///
/// <para>Both operations touch the disk, so both are for background callers only —
/// <see cref="Jobs.MusicSessionJob"/> in practice.</para>
/// </summary>
public interface IMusicPlaylistStore
{
    /// <summary>The stored queue in order. Empty when nothing was ever saved.</summary>
    IReadOnlyList<MusicTrack> Load();

    /// <summary>
    /// Replaces the stored queue with <paramref name="tracks"/>. Best-effort: a queue that cannot be
    /// written is not worth interrupting playback over.
    /// </summary>
    void Save(IEnumerable<MusicTrack> tracks);
}

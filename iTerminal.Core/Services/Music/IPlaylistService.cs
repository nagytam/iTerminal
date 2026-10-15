using iTerminal.Core.Data.Music;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// Playlist I/O: turning files and folders into tracks, and reading/writing M3U. Playlists are kept
/// as plain <c>.m3u8</c> files rather than database rows so they interoperate with every other
/// player and can be browsed, copied and edited in the file panels like any other file.
/// </summary>
public interface IPlaylistService
{
    /// <summary>Extensions recognised as playlist files.</summary>
    IReadOnlySet<string> PlaylistExtensions { get; }

    /// <summary>True when the path names an M3U/M3U8 playlist.</summary>
    bool IsPlaylistFile(string path);

    /// <summary>
    /// Expands a path into tracks: a single audio file yields one track, a folder yields its audio
    /// files (recursively when asked), and a playlist file yields its entries. Tags are read here,
    /// which is why callers must invoke this off the render thread.
    /// </summary>
    IReadOnlyList<MusicTrack> CreateTracks(string path, bool recursive = true);

    /// <summary>Parses an M3U/M3U8 file, resolving relative entries against its own folder.</summary>
    IReadOnlyList<MusicTrack> Load(string path);

    /// <summary>Writes an extended M3U8 playlist.</summary>
    void Save(string path, IEnumerable<MusicTrack> tracks);

    /// <summary>
    /// Where the playing queue used to be auto-saved, before it moved into the database
    /// (<see cref="IMusicPlaylistStore"/>). Nothing writes this file any more; it is kept only so an
    /// upgrade can read the queue one last time.
    /// </summary>
    string LastPlaylistPath { get; }

    /// <summary>
    /// Reads the pre-database queue file. Empty when there is none — which is the normal case on any
    /// installation that never ran the older version.
    /// </summary>
    IReadOnlyList<MusicTrack> LoadLast();
}

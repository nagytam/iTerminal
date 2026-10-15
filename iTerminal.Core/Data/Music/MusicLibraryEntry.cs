namespace iTerminal.Core.Data.Music;

/// <summary>
/// One indexed music file, cached in SQLite by
/// <see cref="Services.Music.IMusicLibraryService"/>. Holding the metadata in the database is what
/// makes the music player's search instant: a query never re-reads a tag, and a restart does not
/// re-index a library that has not changed.
///
/// <para><see cref="Size"/> and <see cref="ModifiedUtc"/> exist only as a change stamp — a rescan
/// re-reads tags for a file only when one of them moved, so an unchanged library costs a directory
/// walk and nothing more.</para>
/// </summary>
public class MusicLibraryEntry
{
    /// <summary>Full path of the file. The primary key: one row per file on disk.</summary>
    public string Path { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string Album { get; set; } = string.Empty;

    /// <summary>
    /// Playing time in ticks, or 0 when it was not probed. Only formats decodable in managed code
    /// are timed during indexing; anything that would need an ffmpeg process per file is left at 0.
    /// </summary>
    public long DurationTicks { get; set; }

    public long Size { get; set; }

    public DateTimeOffset ModifiedUtc { get; set; }

    public DateTimeOffset IndexedUtc { get; set; }

    /// <summary>Projects the row onto the playlist's track type.</summary>
    public MusicTrack ToTrack() => new(Path)
    {
        Title = Title,
        Artist = Artist,
        Album = Album,
        Duration = DurationTicks > 0 ? TimeSpan.FromTicks(DurationTicks) : TimeSpan.Zero,
    };
}

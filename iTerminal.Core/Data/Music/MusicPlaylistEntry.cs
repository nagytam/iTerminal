namespace iTerminal.Core.Data.Music;

/// <summary>
/// One row of the *playing queue*, persisted so the queue survives a restart.
///
/// <para>This is deliberately not the same thing as a playlist file. A saved <c>.m3u8</c> is a
/// document the user made, browsable and editable in the panels and readable by every other player;
/// the queue is session state — what happened to be cued when the app closed — and a file for it
/// only ever existed because there was nowhere else to put it. Rows keep the tags alongside the
/// path so a restored queue lists artists and durations without re-reading a single tag.</para>
/// </summary>
public class MusicPlaylistEntry
{
    /// <summary>Position in the queue, and the primary key: the queue is an ordered list.</summary>
    public int Position { get; set; }

    public string Path { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string Album { get; set; } = string.Empty;

    /// <summary>Playing time in ticks, or 0 when it was never probed.</summary>
    public long DurationTicks { get; set; }

    /// <summary>Projects the row back onto the player's track type.</summary>
    public MusicTrack ToTrack() => new(Path)
    {
        Title = Title,
        Artist = Artist,
        Album = Album,
        Duration = DurationTicks > 0 ? TimeSpan.FromTicks(DurationTicks) : TimeSpan.Zero,
    };

    /// <summary>Captures a track at its place in the queue.</summary>
    public static MusicPlaylistEntry FromTrack(MusicTrack track, int position) => new()
    {
        Position = position,
        Path = track.Path,
        Title = track.Title,
        Artist = track.Artist,
        Album = track.Album,
        DurationTicks = track.Duration.Ticks,
    };
}

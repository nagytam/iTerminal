namespace iTerminal.Core.Data.Music;

/// <summary>What a search hit stands for. Ordered the way results are listed.</summary>
public enum MusicSearchResultKind
{
    Artist,
    Album,
    Track,
}

/// <summary>
/// One row of a library search. Artists and albums are collapsed into a single hit each so that
/// "add everything by this artist" is one keystroke instead of a hundred; a track hit carries the
/// track itself.
/// </summary>
/// <param name="Kind">Whether this hit is an artist, an album or a single track.</param>
/// <param name="Artist">Artist name; empty when the library had no artist tag.</param>
/// <param name="Album">Album name; empty for artist and untagged track hits.</param>
/// <param name="Track">The track, for <see cref="MusicSearchResultKind.Track"/> hits only.</param>
/// <param name="TrackCount">How many tracks this hit expands to.</param>
public sealed record MusicSearchResult(
    MusicSearchResultKind Kind,
    string Artist,
    string Album,
    MusicTrack? Track,
    int TrackCount)
{
    /// <summary>Total playing time of the tracks behind this hit, as far as it is known.</summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>Row label: the artist, the album with its artist, or the track's display name.</summary>
    public string DisplayName => Kind switch
    {
        MusicSearchResultKind.Artist => string.IsNullOrWhiteSpace(Artist) ? "Unknown artist" : Artist,
        MusicSearchResultKind.Album => string.IsNullOrWhiteSpace(Artist)
            ? (string.IsNullOrWhiteSpace(Album) ? "Unknown album" : Album)
            : $"{Album} — {Artist}",
        _ => Track?.DisplayName ?? string.Empty,
    };

    /// <summary>Single-letter tag shown in front of the row.</summary>
    public string KindGlyph => Kind switch
    {
        MusicSearchResultKind.Artist => "A",
        MusicSearchResultKind.Album => "L",
        _ => "·",
    };
}

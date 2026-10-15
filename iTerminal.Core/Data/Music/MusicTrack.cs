namespace iTerminal.Core.Data.Music;

/// <summary>
/// One entry in a playlist. <see cref="Path"/> is the only identity that matters — the metadata
/// fields are best-effort and may stay empty when a file carries no tags or cannot be read.
/// Tracks are value-compared by path so the same file added twice collapses in de-duplication.
/// </summary>
public sealed record MusicTrack(string Path)
{
    /// <summary>Track title from tags; falls back to the file name without extension.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Performing artist from tags; empty when unknown.</summary>
    public string Artist { get; init; } = string.Empty;

    /// <summary>Album from tags; empty when unknown.</summary>
    public string Album { get; init; } = string.Empty;

    /// <summary>
    /// Playing time. <see cref="TimeSpan.Zero"/> means "not known yet" — durations are only filled
    /// in once a decoder has opened the file, since probing every file on add would stall the UI.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>File name without directory, used when no tag-derived title exists.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Title if tagged, otherwise the bare file name — never empty for a real path.</summary>
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title)
            ? Title
            : System.IO.Path.GetFileNameWithoutExtension(Path) is { Length: > 0 } name
                ? name
                : Path;

    /// <summary>"Artist - Title" when the artist is known, otherwise just the title.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Artist) ? DisplayTitle : $"{Artist} - {DisplayTitle}";

    public bool Equals(MusicTrack? other) =>
        other is not null && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Path);
}

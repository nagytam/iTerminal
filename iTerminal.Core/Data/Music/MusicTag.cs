namespace iTerminal.Core.Data.Music;

/// <summary>
/// One tag as it is shown to the user: a display name ("Album artist") and its value. Tags keep the
/// order they appear in the file rather than being sorted, because a tagger writes the fields a
/// human cares about first and that ordering is information in itself.
/// </summary>
public readonly record struct MusicTag(string Name, string Value);

/// <summary>
/// Everything a file's tag block says. <see cref="Title"/>, <see cref="Artist"/> and
/// <see cref="Album"/> are called out separately because they are the three fields every part of the
/// app displays; they are also present in <see cref="Tags"/>, so a viewer that lists everything does
/// not have to special-case them back in.
/// </summary>
public sealed record MusicTagSet
{
    public static readonly MusicTagSet Empty = new();

    /// <summary>Where the tags came from, e.g. "ID3v2.3", "ID3v1" or "Vorbis comment". Empty when none were found.</summary>
    public string Source { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Artist { get; init; } = string.Empty;

    public string Album { get; init; } = string.Empty;

    public IReadOnlyList<MusicTag> Tags { get; init; } = [];

    public bool IsEmpty => Tags.Count == 0;
}

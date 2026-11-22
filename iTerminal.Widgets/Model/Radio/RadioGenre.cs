namespace iTerminal.Widgets.Model.Radio;

/// <summary>
/// The one-cell icon that stands for a station's genre in the list. The genre is free text (<c>music, oldies</c>), so
/// the first rule whose word appears in it wins; the more specific words come first, so <c>business, news</c> is a
/// business station and <c>music, oldies</c> is an oldies one rather than a pop one.
/// </summary>
public static class RadioGenre
{
    /// <summary>A station with no genre at all.</summary>
    public const string NoGenreIcon = "•";

    /// <summary>A genre none of the rules knows, still music or talk of some kind.</summary>
    public const string OtherIcon = "♪";

    private static readonly (string Word, string Icon)[] Rules =
    [
        ("business", "$"),
        ("technology", "◈"),
        ("news", "¶"),
        ("talk", "¶"),
        ("sport", "◎"),
        ("jazz", "♭"),
        ("classical", "♩"),
        ("electronic", "»"),
        ("techno", "»"),
        ("trance", "»"),
        ("dance", "»"),
        ("ambient", "~"),
        ("chill", "~"),
        ("smooth", "~"),
        ("lounge", "~"),
        ("rock", "♬"),
        ("alternative", "♬"),
        ("indie", "♬"),
        ("punk", "♬"),
        ("metal", "♬"),
        ("oldies", "↺"),
        ("retro", "↺"),
        ("80s", "↺"),
        ("90s", "↺"),
        ("70s", "↺"),
        ("pop", "♫"),
        ("hits", "♫"),
        ("contemporary", "♫"),
    ];

    /// <summary>The icon for <paramref name="genre"/>: a note for an unknown music genre, a bullet when there is no genre.</summary>
    public static string IconFor(string? genre)
    {
        if (string.IsNullOrWhiteSpace(genre))
        {
            return NoGenreIcon;
        }

        foreach (var (word, icon) in Rules)
        {
            if (genre.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return icon;
            }
        }

        return OtherIcon;
    }
}

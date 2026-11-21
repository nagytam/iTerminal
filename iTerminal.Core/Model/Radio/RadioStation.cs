using iConsole.Media.Audio;

namespace iTerminal.Core.Model.Radio;

/// <summary>
/// An internet radio station: a name the person chose, the HTTP(S) address its stream comes from, an optional genre and
/// whether it is a favourite. Immutable, so a change is a <c>with</c> copy written back by <see cref="RadioStationStore.Update"/>.
/// </summary>
public sealed record RadioStation(Guid Id, string Name, string Url, string Genre, bool IsFavourite)
{
    /// <summary>True when every word of <paramref name="query"/> appears (any case) in the name, genre or address.</summary>
    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.All(word =>
            Name.Contains(word, StringComparison.OrdinalIgnoreCase)
            || Genre.Contains(word, StringComparison.OrdinalIgnoreCase)
            || Url.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Checks what the person typed: a name is needed, and the address must be an absolute http or https URL.</summary>
    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = iConsole.Localization.UiText.Get("Radio.NeedsName", "A station needs a name");
            return false;
        }

        if (!AudioDecoderFactory.IsStreamUrl(Url))
        {
            error = iConsole.Localization.UiText.Get("Radio.NeedsUrl", "The stream address must start with http:// or https://");
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>The same station with its text fields trimmed.</summary>
    public RadioStation Trimmed() => this with
    {
        Name = Name.Trim(),
        Url = Url.Trim(),
        Genre = Genre.Trim(),
    };
}

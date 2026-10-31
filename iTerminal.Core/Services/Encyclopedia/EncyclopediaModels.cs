namespace iTerminal.Core.Services.Encyclopedia;

/// <summary>How a source is reached: through the MediaWiki API, or only by opening its own search page.</summary>
public enum EncyclopediaKind
{
    /// <summary>A Wikipedia or Wiktionary: searched and read through the open MediaWiki API.</summary>
    MediaWiki,

    /// <summary>An encyclopedia with no interface a program may use (or one behind a bot check): its search page is opened in the browser.</summary>
    LinkOut,
}

/// <summary>
/// One place to look a term up. For a <see cref="EncyclopediaKind.MediaWiki"/> source <see cref="Address"/> is the host
/// (<c>en.wikipedia.org</c>); for a <see cref="EncyclopediaKind.LinkOut"/> one it is the search address, with <c>{0}</c> where the term goes.
/// </summary>
public sealed record EncyclopediaSource(string Id, string Name, EncyclopediaKind Kind, string Address)
{
    /// <summary>The page the browser should open for a term (or an article title) at this source.</summary>
    public string PageUrl(string term) => Kind == EncyclopediaKind.LinkOut
        ? string.Format(System.Globalization.CultureInfo.InvariantCulture, Address, Uri.EscapeDataString(term))
        : $"https://{Address}/wiki/{Uri.EscapeDataString(term.Replace(' ', '_'))}";
}

/// <summary>The places that can be searched. The first is the default.</summary>
public static class EncyclopediaSources
{
    public static readonly IReadOnlyList<EncyclopediaSource> All =
    [
        new("wikipedia-en", "Wikipedia (English)", EncyclopediaKind.MediaWiki, "en.wikipedia.org"),
        new("wikipedia-de", "Wikipedia (Deutsch)", EncyclopediaKind.MediaWiki, "de.wikipedia.org"),
        new("wikipedia-fr", "Wikipedia (Français)", EncyclopediaKind.MediaWiki, "fr.wikipedia.org"),
        new("wikipedia-es", "Wikipedia (Español)", EncyclopediaKind.MediaWiki, "es.wikipedia.org"),
        new("wikipedia-it", "Wikipedia (Italiano)", EncyclopediaKind.MediaWiki, "it.wikipedia.org"),
        new("wikipedia-hu", "Wikipedia (Magyar)", EncyclopediaKind.MediaWiki, "hu.wikipedia.org"),
        new("wikipedia-simple", "Simple English Wikipedia", EncyclopediaKind.MediaWiki, "simple.wikipedia.org"),
        new("wiktionary-en", "Wiktionary (English)", EncyclopediaKind.MediaWiki, "en.wiktionary.org"),
        new("britannica", "Britannica", EncyclopediaKind.LinkOut, "https://www.britannica.com/search?query={0}"),
    ];

    /// <summary>The source with the id, or the default one when the id is unknown (a setting from an older version).</summary>
    public static EncyclopediaSource Find(string? id) => All.FirstOrDefault(source => source.Id == id) ?? All[0];
}

/// <summary>A search result: the title of an article and a line of text around the match.</summary>
public sealed record EncyclopediaHit(string Title, string Snippet);

/// <summary>An article as plain text. <see cref="IsFull"/> is false for the introduction alone.</summary>
public sealed record EncyclopediaArticle(string Title, string Text, string Url, bool IsFull);

/// <summary>A look-up that failed in a way worth telling the person: no network, an answer that made no sense, no such article.</summary>
public sealed class EncyclopediaException : Exception
{
    public EncyclopediaException(string message)
        : base(message)
    {
    }

    public EncyclopediaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

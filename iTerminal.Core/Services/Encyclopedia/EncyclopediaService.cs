using iConsole.Localization;

namespace iTerminal.Core.Services.Encyclopedia;

public interface IEncyclopediaService
{
    /// <summary>Where a term can be looked up.</summary>
    IReadOnlyList<EncyclopediaSource> Sources { get; }

    /// <summary>The articles that match the term. A link-out source answers with one entry, the term itself.</summary>
    Task<IReadOnlyList<EncyclopediaHit>> SearchAsync(EncyclopediaSource source, string query, CancellationToken cancellationToken = default);

    /// <summary>The article called <paramref name="title"/>: its introduction, or all of it.</summary>
    Task<EncyclopediaArticle> GetArticleAsync(EncyclopediaSource source, string title, bool full, CancellationToken cancellationToken = default);
}

/// <summary>
/// Looks terms up. The network work is the provider's; what is added here is the part no provider can do: the sources that
/// cannot be read by a program (the person is sent to their search page), and a small memory of the articles read, so going
/// back to a result in the list does not ask the network again.
/// </summary>
public sealed class EncyclopediaService(IEncyclopediaProvider provider) : IEncyclopediaService
{
    internal const int CacheLimit = 40;

    private readonly Dictionary<string, EncyclopediaArticle> _cache = [];
    private readonly object _gate = new();

    public IReadOnlyList<EncyclopediaSource> Sources => EncyclopediaSources.All;

    public Task<IReadOnlyList<EncyclopediaHit>> SearchAsync(EncyclopediaSource source, string query, CancellationToken cancellationToken = default)
    {
        string term = query.Trim();
        if (term.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<EncyclopediaHit>>([]);
        }

        if (source.Kind == EncyclopediaKind.LinkOut)
        {
            return Task.FromResult<IReadOnlyList<EncyclopediaHit>>([new EncyclopediaHit(term, string.Empty)]);
        }

        return provider.SearchAsync(source, term, cancellationToken);
    }

    public async Task<EncyclopediaArticle> GetArticleAsync(EncyclopediaSource source, string title, bool full, CancellationToken cancellationToken = default)
    {
        if (source.Kind == EncyclopediaKind.LinkOut)
        {
            return new EncyclopediaArticle(
                title,
                string.Format(UiText.Get("Encyclopedia.LinkOut", "{0} cannot be read from here: it has no interface a program may use. Press F2 to open the search for \"{1}\" in your browser."), source.Name, title),
                source.PageUrl(title),
                true);
        }

        string key = $"{source.Id}|{title}|{full}";
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out EncyclopediaArticle? cached))
            {
                return cached;
            }
        }

        EncyclopediaArticle article = await provider.GetArticleAsync(source, title, full, cancellationToken);
        if (!full && article.Text.Length == 0)
        {
            // A dictionary entry starts with a heading, so it has no introduction; showing "no text" for a word that has an
            // entry would be wrong, and the whole of a dictionary entry is short.
            article = await provider.GetArticleAsync(source, title, true, cancellationToken);
        }
        lock (_gate)
        {
            if (_cache.Count >= CacheLimit)
            {
                _cache.Clear();
            }

            _cache[key] = article;
        }

        return article;
    }
}

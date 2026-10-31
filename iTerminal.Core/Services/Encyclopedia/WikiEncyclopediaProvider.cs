using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Services.Encyclopedia;

/// <summary>Reads articles from a source on the network.</summary>
public interface IEncyclopediaProvider
{
    /// <summary>The articles that match the term, best first. Only for <see cref="EncyclopediaKind.MediaWiki"/> sources.</summary>
    Task<IReadOnlyList<EncyclopediaHit>> SearchAsync(EncyclopediaSource source, string query, CancellationToken cancellationToken = default);

    /// <summary>The article, as plain text: its introduction, or all of it.</summary>
    Task<EncyclopediaArticle> GetArticleAsync(EncyclopediaSource source, string title, bool full, CancellationToken cancellationToken = default);
}

/// <summary>
/// Wikipedia and Wiktionary through the MediaWiki Action API: no key, a user agent that says who is asking (the Wikimedia
/// rule), a search that returns titles with a line of text, and an extract that comes back as plain text.
/// </summary>
public sealed partial class WikiEncyclopediaProvider : IEncyclopediaProvider
{
    internal const int ResultLimit = 15;

    private static readonly HttpClient SharedClient = CreateClient();

    private readonly HttpClient _client;

    public WikiEncyclopediaProvider()
        : this(SharedClient)
    {
    }

    internal WikiEncyclopediaProvider(HttpClient client)
    {
        _client = client;
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("iTerminal/1.0 (+https://github.com/nagytam/iTerminal)");
        return client;
    }

    public async Task<IReadOnlyList<EncyclopediaHit>> SearchAsync(EncyclopediaSource source, string query, CancellationToken cancellationToken = default)
    {
        string url = SearchUrl(source, query);
        return ParseSearch(await GetAsync(source, url, cancellationToken));
    }

    public async Task<EncyclopediaArticle> GetArticleAsync(EncyclopediaSource source, string title, bool full, CancellationToken cancellationToken = default)
    {
        string url = ArticleUrl(source, title, full);
        return ParseArticle(source, title, full, await GetAsync(source, url, cancellationToken));
    }

    private async Task<string> GetAsync(EncyclopediaSource source, string url, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.GetStringAsync(url, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new EncyclopediaException($"Could not reach {source.Name}: {exception.Message}", exception);
        }
    }

    internal static string SearchUrl(EncyclopediaSource source, string query) =>
        $"https://{source.Address}/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&srlimit={ResultLimit}&srprop=snippet&format=json&formatversion=2";

    internal static string ArticleUrl(EncyclopediaSource source, string title, bool full) =>
        $"https://{source.Address}/w/api.php?action=query&prop=extracts%7Cinfo&explaintext=1&exsectionformat=plain&redirects=1&inprop=url"
        + (full ? string.Empty : "&exintro=1")
        + $"&titles={Uri.EscapeDataString(title)}&format=json&formatversion=2";

    /// <summary>Reads the <c>query.search</c> list: a title and a snippet with the match marked by tags, which are dropped.</summary>
    internal static IReadOnlyList<EncyclopediaHit> ParseSearch(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("query", out JsonElement query)
                || !query.TryGetProperty("search", out JsonElement search)
                || search.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            List<EncyclopediaHit> hits = [];
            foreach (JsonElement item in search.EnumerateArray())
            {
                string title = item.TryGetProperty("title", out JsonElement t) ? t.GetString() ?? string.Empty : string.Empty;
                if (title.Length == 0)
                {
                    continue;
                }

                string snippet = item.TryGetProperty("snippet", out JsonElement s) ? PlainText(s.GetString() ?? string.Empty) : string.Empty;
                hits.Add(new EncyclopediaHit(title, snippet));
            }

            return hits;
        }
        catch (JsonException exception)
        {
            throw new EncyclopediaException("The answer was not what was expected from the search.", exception);
        }
    }

    /// <summary>Reads the first page of <c>query.pages</c>: its title, its extract and its address; a missing page is an error.</summary>
    internal static EncyclopediaArticle ParseArticle(EncyclopediaSource source, string requestedTitle, bool full, string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("query", out JsonElement query)
                || !query.TryGetProperty("pages", out JsonElement pages)
                || pages.ValueKind != JsonValueKind.Array
                || pages.GetArrayLength() == 0)
            {
                throw new EncyclopediaException($"{source.Name} has no article called \"{requestedTitle}\".");
            }

            JsonElement page = pages[0];
            if (page.TryGetProperty("missing", out JsonElement missing) && missing.ValueKind == JsonValueKind.True)
            {
                throw new EncyclopediaException($"{source.Name} has no article called \"{requestedTitle}\".");
            }

            string title = page.TryGetProperty("title", out JsonElement t) ? t.GetString() ?? requestedTitle : requestedTitle;
            string text = page.TryGetProperty("extract", out JsonElement e) ? (e.GetString() ?? string.Empty).Trim() : string.Empty;
            string url = page.TryGetProperty("fullurl", out JsonElement u) && u.GetString() is { Length: > 0 } fullUrl ? fullUrl : source.PageUrl(title);
            return new EncyclopediaArticle(title, text, url, full);
        }
        catch (JsonException exception)
        {
            throw new EncyclopediaException("The answer was not what was expected from the article.", exception);
        }
    }

    /// <summary>The search marks the matched words with tags and escapes the rest as HTML.</summary>
    internal static string PlainText(string html) => WebUtility.HtmlDecode(TagPattern().Replace(html, string.Empty)).Trim();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();
}

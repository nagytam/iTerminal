using System.Text.Json;

namespace iTerminal.Core.Services.Web;

public sealed record WebBookmark(string Url, string Title);

/// <summary>
/// What the Web Browser remembers between runs: bookmarks, the pages visited (newest first, capped), the
/// home page and the search engine. Kept in the settings like the calendar and the to-do list are; a
/// stored value that cannot be read is treated as empty rather than stopping the browser from opening.
/// </summary>
public sealed class WebBrowserStore(ISettingsService settingsService)
{
    public const string BookmarksKey = "WebBrowser.Bookmarks";
    public const string HistoryKey = "WebBrowser.History";
    public const string HomeKey = "WebBrowser.Home";
    public const string SearchKey = "WebBrowser.SearchUrl";
    public const string DefaultHome = "about:home";
    public const int MaxHistory = 300;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly object _lock = new();
    private List<WebBookmark>? _bookmarks;
    private List<WebBookmark>? _history;

    public string Home => settingsService.Get(HomeKey) is { Length: > 0 } home ? home : DefaultHome;

    /// <summary>The address template a search is sent to; <c>{0}</c> is the escaped query.</summary>
    public string? SearchUrl => settingsService.Get(SearchKey) is { Length: > 0 } search && search.Contains("{0}", StringComparison.Ordinal) ? search : null;

    public IReadOnlyList<WebBookmark> Bookmarks
    {
        get
        {
            lock (_lock)
            {
                return [.. Bookmarked()];
            }
        }
    }

    /// <summary>The visited pages, the latest first, each address once.</summary>
    public IReadOnlyList<WebBookmark> History
    {
        get
        {
            lock (_lock)
            {
                return [.. Visited()];
            }
        }
    }

    public bool IsBookmarked(string url)
    {
        lock (_lock)
        {
            return Bookmarked().Any(b => b.Url == url);
        }
    }

    /// <summary>Adds the bookmark, or removes it when the address is already one. Returns true when it is now bookmarked.</summary>
    public bool ToggleBookmark(string url, string title)
    {
        lock (_lock)
        {
            var list = Bookmarked();
            var existing = list.FindIndex(b => b.Url == url);
            if (existing >= 0)
            {
                list.RemoveAt(existing);
            }
            else
            {
                list.Add(new WebBookmark(url, Clean(title, url)));
            }

            Save(BookmarksKey, list);
            return existing < 0;
        }
    }

    public void RemoveBookmark(string url)
    {
        lock (_lock)
        {
            var list = Bookmarked();
            if (list.RemoveAll(b => b.Url == url) > 0)
            {
                Save(BookmarksKey, list);
            }
        }
    }

    public void AddVisit(string url, string title)
    {
        lock (_lock)
        {
            var list = Visited();
            list.RemoveAll(h => h.Url == url);
            list.Insert(0, new WebBookmark(url, Clean(title, url)));
            if (list.Count > MaxHistory)
            {
                list.RemoveRange(MaxHistory, list.Count - MaxHistory);
            }

            Save(HistoryKey, list);
        }
    }

    public void RemoveVisit(string url)
    {
        lock (_lock)
        {
            var list = Visited();
            if (list.RemoveAll(h => h.Url == url) > 0)
            {
                Save(HistoryKey, list);
            }
        }
    }

    public void ClearHistory()
    {
        lock (_lock)
        {
            Visited().Clear();
            Save(HistoryKey, Visited());
        }
    }

    private static string Clean(string title, string fallback)
    {
        var text = title.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length == 0 ? fallback : text.Length > 200 ? text[..200] : text;
    }

    private List<WebBookmark> Bookmarked() => _bookmarks ??= Load(BookmarksKey);

    private List<WebBookmark> Visited() => _history ??= Load(HistoryKey);

    private List<WebBookmark> Load(string key)
    {
        try
        {
            var json = settingsService.Get(key);
            // A null entry ("[null]") is not a page; the rest of the list is still good.
            return string.IsNullOrWhiteSpace(json) ? [] : [.. (JsonSerializer.Deserialize<List<WebBookmark>>(json, Json) ?? []).Where(page => page is not null)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(string key, List<WebBookmark> list)
    {
        settingsService.Set(key, JsonSerializer.Serialize(list, Json));
        settingsService.Write();
    }
}

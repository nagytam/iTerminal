using System.Net;
using System.Text;
using iTerminal.Core.Services.Web;

namespace iTerminal.Core.Model.Web;

/// <summary>One page in a tab's history, with what is needed to show it again: the parsed page, where it was scrolled to and which item had the focus.</summary>
internal sealed class WebHistoryEntry
{
    public required WebFetchRequest Request { get; init; }
    public required string Url { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Status { get; set; } = 200;
    public string StatusText { get; set; } = string.Empty;

    /// <summary>The rendered page; null for plain text and once the entry has been dropped to save memory.</summary>
    public WebDocument? Document { get; set; }

    /// <summary>A page shown as it is (text files, the page source).</summary>
    public string? PlainText { get; set; }

    /// <summary>The page's source text, for the source view of an HTML page.</summary>
    public string? Source { get; set; }

    public WebFetchResponse? Response { get; set; }
    public WebPage? Page { get; set; }

    /// <summary>Whether <see cref="Page"/> shows the page source rather than the rendered page.</summary>
    public bool PageIsSource { get; set; }
    public int Scroll { get; set; }
    public int FocusedItem { get; set; } = -1;
    public bool IsDropped => Document is null && PlainText is null;
    public object LayoutLock { get; } = new();
    public HtmlLayout? Layout { get; set; }
}

/// <summary>
/// One tab: its history and the page showing. Loading happens on a background task and lands here under
/// <see cref="Sync"/>, so the dialog can read what to draw at any moment without waiting for the network.
/// </summary>
internal sealed class WebTab
{
    public const int MaxHistory = 100;
    private const int KeepLoaded = 8;

    public object Sync { get; } = new();
    public List<WebHistoryEntry> History { get; } = [];
    public int Index { get; set; } = -1;
    public bool IsLoading { get; set; }
    public string LoadingUrl { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool ShowSource { get; set; }
    public int Generation { get; set; }
    public CancellationTokenSource? Cancellation { get; set; }
    public Task LoadTask { get; set; } = Task.CompletedTask;
    public Task LayoutTask { get; set; } = Task.CompletedTask;
    public int LayoutWidth { get; set; }
    public string? PendingFragment { get; set; }

    /// <summary>Changes whenever something the screen shows has; lets a caller notice without comparing state.</summary>
    public int Version { get; private set; }

    public WebHistoryEntry? Current => Index >= 0 && Index < History.Count ? History[Index] : null;

    public string Title
    {
        get
        {
            lock (Sync)
            {
                if (IsLoading && Current is null)
                {
                    return LoadingUrl;
                }

                var entry = Current;
                return entry is null ? "New tab" : entry.Title.Length > 0 ? entry.Title : entry.Url;
            }
        }
    }

    public string Url
    {
        get
        {
            lock (Sync)
            {
                return IsLoading && LoadingUrl.Length > 0 ? LoadingUrl : Current?.Url ?? string.Empty;
            }
        }
    }

    public bool CanGoBack
    {
        get
        {
            lock (Sync)
            {
                return Index > 0;
            }
        }
    }

    public bool CanGoForward
    {
        get
        {
            lock (Sync)
            {
                return Index >= 0 && Index < History.Count - 1;
            }
        }
    }

    public void Touch() => Version++;

    /// <summary>The page laid out for the current width, or null while there is none yet. Caller holds <see cref="Sync"/> or accepts a stale read.</summary>
    public WebPage? Page => Current?.Page;

    public int Scroll => Current?.Scroll ?? 0;

    public int FocusedItem => Current?.FocusedItem ?? -1;

    /// <summary>Scrolls so that <paramref name="line"/> is the first visible one, kept within the page.</summary>
    public void ScrollTo(int line, int viewHeight)
    {
        lock (Sync)
        {
            if (Current is not { } entry)
            {
                return;
            }

            var count = entry.Page?.Lines.Count ?? 0;
            entry.Scroll = Math.Clamp(line, 0, Math.Max(0, count - Math.Max(1, viewHeight)));
            Touch();
        }
    }

    public void ScrollBy(int lines, int viewHeight)
    {
        int current;
        lock (Sync)
        {
            current = Current?.Scroll ?? 0;
        }

        ScrollTo(current + lines, viewHeight);
    }

    /// <summary>Moves the focus to the next or previous link or control, scrolling it into view. Returns false when the page has none.</summary>
    public bool MoveFocus(bool forward, int viewHeight)
    {
        lock (Sync)
        {
            if (Current is not { Page: { } page } entry || page.Items.Count == 0)
            {
                return false;
            }

            var next = entry.FocusedItem;
            if (next < 0)
            {
                // The first one on screen, or the last one on screen when going backwards.
                var top = entry.Scroll;
                var bottom = entry.Scroll + Math.Max(1, viewHeight) - 1;
                next = forward
                    ? FindIndex(page, i => page.Items[i].Line >= top)
                    : FindLastIndex(page, i => page.Items[i].Line <= bottom);
                if (next < 0)
                {
                    next = forward ? 0 : page.Items.Count - 1;
                }
            }
            else
            {
                next = (next + (forward ? 1 : -1) + page.Items.Count) % page.Items.Count;
            }

            entry.FocusedItem = next;
            EnsureVisible(entry, page.Items[next].Line, viewHeight);
            Touch();
            return true;
        }
    }

    private static int FindIndex(WebPage page, Func<int, bool> match)
    {
        for (var i = 0; i < page.Items.Count; i++)
        {
            if (match(i))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindLastIndex(WebPage page, Func<int, bool> match)
    {
        for (var i = page.Items.Count - 1; i >= 0; i--)
        {
            if (match(i))
            {
                return i;
            }
        }

        return -1;
    }

    private static void EnsureVisible(WebHistoryEntry entry, int line, int viewHeight)
    {
        var height = Math.Max(1, viewHeight);
        if (line < entry.Scroll)
        {
            entry.Scroll = line;
        }
        else if (line >= entry.Scroll + height)
        {
            entry.Scroll = line - height + 1;
        }

        var count = entry.Page?.Lines.Count ?? 0;
        entry.Scroll = Math.Clamp(entry.Scroll, 0, Math.Max(0, count - height));
    }

    /// <summary>The item under the focus, if any.</summary>
    public WebItem? FocusedWebItem
    {
        get
        {
            lock (Sync)
            {
                return Current is { Page: { } page, FocusedItem: >= 0 } entry && entry.FocusedItem < page.Items.Count ? page.Items[entry.FocusedItem] : null;
            }
        }
    }

    internal void DropOldEntries()
    {
        for (var i = 0; i < History.Count; i++)
        {
            if (Math.Abs(i - Index) > KeepLoaded && !History[i].IsDropped)
            {
                var entry = History[i];
                entry.Document = null;
                entry.PlainText = null;
                entry.Source = null;
                entry.Response = null;
                entry.Page = null;
                entry.Layout = null;
            }
        }
    }
}

/// <summary>
/// The Web Browser without its screen: tabs, navigation, history, forms and downloads. The dialog only
/// draws it and turns keys into calls. Everything that touches the network runs on a background task, and
/// only the newest request of a tab may land.
/// </summary>
internal sealed class WebBrowserModel(IWebFetcher fetcher, WebBrowserStore store, string downloadDirectory)
{
    private const int MaxRefreshHops = 5;

    private readonly List<WebTab> _tabs = [new WebTab()];
    private int _active;
    private readonly object _lock = new();

    public WebBrowserStore Store => store;
    public string DownloadDirectory => downloadDirectory;

    public IReadOnlyList<WebTab> Tabs
    {
        get
        {
            lock (_lock)
            {
                return [.. _tabs];
            }
        }
    }

    public int ActiveIndex
    {
        get
        {
            lock (_lock)
            {
                return _active;
            }
        }
    }

    public WebTab Active
    {
        get
        {
            lock (_lock)
            {
                return _tabs[_active];
            }
        }
    }

    #region Tabs

    public WebTab NewTab()
    {
        lock (_lock)
        {
            var tab = new WebTab();
            _tabs.Insert(_active + 1, tab);
            _active++;
            return tab;
        }
    }

    /// <summary>Closes a tab. The last tab is not closed: it goes back to a blank page.</summary>
    public bool CloseTab(int index)
    {
        WebTab? closed;
        var onlyTab = false;
        lock (_lock)
        {
            if (index < 0 || index >= _tabs.Count)
            {
                return false;
            }

            if (_tabs.Count == 1)
            {
                onlyTab = true;
                closed = _tabs[0];
                _tabs[0] = new WebTab();
            }
            else
            {
                closed = _tabs[index];
                _tabs.RemoveAt(index);
                if (_active > index || _active >= _tabs.Count)
                {
                    _active = Math.Max(0, _active - 1);
                }
            }
        }

        Stop(closed);
        return !onlyTab;
    }

    public void SelectTab(int index)
    {
        lock (_lock)
        {
            if (index >= 0 && index < _tabs.Count)
            {
                _active = index;
            }
        }
    }

    public void SelectNext(int step)
    {
        lock (_lock)
        {
            _active = (_active + step + _tabs.Count) % _tabs.Count;
        }
    }

    #endregion

    #region Navigation

    /// <summary>Opens what was typed into the address bar: an address, a path, or words to search for.</summary>
    public Task NavigateAsync(WebTab tab, string input)
    {
        var address = WebAddress.FromInput(input, store.SearchUrl);
        return OpenAsync(tab, new WebFetchRequest(address));
    }

    public Task OpenAsync(WebTab tab, WebFetchRequest request, bool replace = false)
    {
        // A "#fragment" on the page already open is only a scroll.
        if (!request.IsPost && request.Url.Contains('#') && !replace)
        {
            lock (tab.Sync)
            {
                if (tab.Current is { } current
                    && !current.IsDropped
                    && WebAddress.IsSameDocument(current.Url, request.Url)
                    && !tab.IsLoading)
                {
                    PushSameDocument(tab, current, request);
                    return Task.CompletedTask;
                }
            }
        }

        return LoadAsync(tab, request, replace);
    }

    private void PushSameDocument(WebTab tab, WebHistoryEntry current, WebFetchRequest request)
    {
        current.Scroll = Math.Max(0, current.Scroll);
        var entry = new WebHistoryEntry
        {
            Request = request,
            Url = request.Url,
            Title = current.Title,
            Status = current.Status,
            StatusText = current.StatusText,
            Document = current.Document,
            PlainText = current.PlainText,
            Source = current.Source,
            Response = current.Response,
            Page = current.Page,
            PageIsSource = current.PageIsSource,
            Layout = current.Layout,
        };
        Push(tab, entry);
        tab.PendingFragment = WebAddress.Fragment(request.Url);
        ApplyFragment(tab, 1);
        tab.Touch();
    }

    public Task ReloadAsync(WebTab tab)
    {
        WebFetchRequest? request;
        lock (tab.Sync)
        {
            request = tab.Current?.Request;
        }

        return request is null ? Task.CompletedTask : LoadAsync(tab, request, replace: true);
    }

    public bool GoBack(WebTab tab) => Go(tab, -1);

    public bool GoForward(WebTab tab) => Go(tab, 1);

    private bool Go(WebTab tab, int step)
    {
        WebHistoryEntry? target;
        lock (tab.Sync)
        {
            var next = tab.Index + step;
            if (next < 0 || next >= tab.History.Count)
            {
                return false;
            }

            Cancel(tab);
            tab.Index = next;
            tab.ShowSource = false;
            tab.PendingFragment = null;
            tab.Message = string.Empty;
            target = tab.History[next];
            tab.Touch();
        }

        if (target.IsDropped)
        {
            _ = LoadAsync(tab, target.Request, replace: true);
        }

        return true;
    }

    public void Stop(WebTab tab)
    {
        lock (tab.Sync)
        {
            if (!tab.IsLoading)
            {
                return;
            }

            Cancel(tab);
            tab.Message = "Stopped.";
            tab.Touch();
        }
    }

    private static void Cancel(WebTab tab)
    {
        tab.Generation++;
        tab.Cancellation?.Cancel();
        tab.Cancellation = null;
        tab.IsLoading = false;
        tab.LoadingUrl = string.Empty;
    }

    private Task LoadAsync(WebTab tab, WebFetchRequest request, bool replace)
    {
        CancellationToken token;
        int generation;
        lock (tab.Sync)
        {
            tab.Cancellation?.Cancel();
            tab.Cancellation = new CancellationTokenSource();
            token = tab.Cancellation.Token;
            generation = ++tab.Generation;
            tab.IsLoading = true;
            tab.LoadingUrl = request.Url;
            tab.Message = "Loading " + request.Url + "…";
            tab.Touch();
        }

        var task = Task.Run(async () =>
        {
            WebHistoryEntry entry;
            try
            {
                entry = await BuildEntryAsync(request, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (tab.Sync)
            {
                if (generation != tab.Generation)
                {
                    return;
                }

                tab.IsLoading = false;
                tab.LoadingUrl = string.Empty;
                tab.ShowSource = false;
                tab.Cancellation = null;
                tab.PendingFragment = WebAddress.Fragment(entry.Url);
                if (replace && tab.Current is { } current)
                {
                    tab.History[tab.Index] = entry;
                    // The place the person was at is kept when the same page is fetched again.
                    if (WebAddress.IsSameDocument(current.Url, entry.Url) && string.IsNullOrEmpty(tab.PendingFragment))
                    {
                        entry.Scroll = current.Scroll;
                    }
                }
                else
                {
                    Push(tab, entry);
                }

                tab.Message = entry.Status is >= 200 and < 300 ? string.Empty : $"{entry.Status} {entry.StatusText}".Trim();
                tab.DropOldEntries();
                tab.Touch();
            }

            if (entry.Status is >= 200 and < 400 && !entry.Url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                store.AddVisit(entry.Url, entry.Title);
            }
        }, CancellationToken.None);

        lock (tab.Sync)
        {
            tab.LoadTask = task;
        }

        return task;
    }

    private static void Push(WebTab tab, WebHistoryEntry entry)
    {
        if (tab.Index < tab.History.Count - 1)
        {
            tab.History.RemoveRange(tab.Index + 1, tab.History.Count - tab.Index - 1);
        }

        tab.History.Add(entry);
        if (tab.History.Count > WebTab.MaxHistory)
        {
            tab.History.RemoveAt(0);
        }

        tab.Index = tab.History.Count - 1;
    }

    private async Task<WebHistoryEntry> BuildEntryAsync(WebFetchRequest request, CancellationToken token)
    {
        try
        {
            var viewSource = request.Url.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase);
            var current = viewSource ? request with { Url = request.Url["view-source:".Length..] } : request;
            WebFetchResponse response;
            var hops = 0;
            while (true)
            {
                response = await fetcher.FetchAsync(current, token);
                if (!viewSource && WebContent.Classify(response) == WebContentKind.Html && hops < MaxRefreshHops)
                {
                    var document = WebDocument.Parse(response.Url, WebContent.Decode(response));
                    if (document.Refresh is { Seconds: <= 5 } refresh && !WebAddress.IsSameDocument(refresh.Url, response.Url))
                    {
                        current = new WebFetchRequest(refresh.Url, Referer: response.Url);
                        hops++;
                        continue;
                    }
                }

                break;
            }

            return CreateEntry(request, response, viewSource);
        }
        catch (WebFetchException exception)
        {
            return ErrorEntry(request, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ErrorEntry(request, exception.Message);
        }
    }

    private WebHistoryEntry CreateEntry(WebFetchRequest request, WebFetchResponse response, bool viewSource)
    {
        var kind = WebContent.Classify(response);
        var entry = new WebHistoryEntry
        {
            Request = request,
            Url = viewSource ? "view-source:" + response.Url : response.Url,
            Status = response.Status,
            StatusText = response.ReasonPhrase,
            Response = response,
        };

        if (viewSource || kind == WebContentKind.Text)
        {
            entry.PlainText = WebContent.Decode(response);
            entry.Title = viewSource ? "Source of " + response.Url : LastSegment(response.Url);
            return entry;
        }

        if (kind == WebContentKind.Html)
        {
            var text = WebContent.Decode(response);
            entry.Source = text;
            entry.Document = WebDocument.Parse(response.Url, text);
            entry.Title = entry.Document.Title;
            return entry;
        }

        var name = response.FileName ?? LastSegment(response.Url);
        entry.Title = name;
        entry.Document = Info(response.Url, "<h1>" + WebUtility.HtmlEncode(name) + "</h1>"
            + "<p>" + WebUtility.HtmlEncode(response.ContentType.Length > 0 ? response.ContentType : "Unknown type") + ", " + FormatSize(response.Content.Length) + "</p>"
            + "<p>This kind of content cannot be shown here. Press <b>F7</b> to save it to " + WebUtility.HtmlEncode(downloadDirectory) + ".</p>");
        return entry;
    }

    private static WebHistoryEntry ErrorEntry(WebFetchRequest request, string message) => new()
    {
        Request = request,
        Url = request.Url,
        Title = "Cannot open this page",
        Status = 0,
        Document = Info(request.Url, "<h1>Cannot open this page</h1><p>" + WebUtility.HtmlEncode(message) + "</p><p>Address: " + WebUtility.HtmlEncode(request.Url) + "</p><p>Press <b>F5</b> to try again, or <b>Ctrl+L</b> to enter another address.</p>"),
    };

    private static WebDocument Info(string url, string body) =>
        WebDocument.Parse(url, "<html><body>" + body + "</body></html>");

    #endregion

    #region Links and forms

    /// <summary>Follows the link, in the same tab or in a new one. Links that are not web addresses are reported, not followed.</summary>
    public Task OpenLinkAsync(WebTab tab, int linkId, bool newTab = false)
    {
        string href;
        string from;
        lock (tab.Sync)
        {
            if (tab.Current is not { Document: { } document } || linkId < 0 || linkId >= document.Links.Count)
            {
                return Task.CompletedTask;
            }

            href = document.Resolve(document.Links[linkId].Href);
            from = tab.Current.Url;
        }

        if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            Say(tab, "This link needs JavaScript, which the Web Browser does not run.");
            return Task.CompletedTask;
        }

        if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
        {
            Say(tab, href);
            return Task.CompletedTask;
        }

        var target = tab;
        if (newTab)
        {
            target = NewTab();
        }

        return OpenAsync(target, new WebFetchRequest(href, Referer: from));
    }

    /// <summary>Sends the form the field belongs to; <paramref name="submitter"/> is the button that was pressed, or null for Enter in a text box.</summary>
    public Task SubmitAsync(WebTab tab, WebField field)
    {
        WebFetchRequest request;
        lock (tab.Sync)
        {
            if (field.Form is not { } form || tab.Current is not { Document: { } document } current)
            {
                return Task.CompletedTask;
            }

            var submitter = field.Kind == WebFieldKind.Submit ? field : form.Fields.FirstOrDefault(f => f.Kind == WebFieldKind.Submit && !f.Disabled);
            var action = form.Action.Length == 0 ? current.Url : document.Resolve(form.Action);
            var query = WebForm.Encode(form.Collect(submitter));
            if (form.IsPost)
            {
                request = new WebFetchRequest(action, "POST", query, current.Url);
            }
            else
            {
                var withoutFragment = WebAddress.WithoutFragment(action);
                var queryStart = withoutFragment.IndexOf('?');
                if (queryStart >= 0)
                {
                    withoutFragment = withoutFragment[..queryStart];
                }

                request = new WebFetchRequest(withoutFragment + "?" + query, Referer: current.Url);
            }
        }

        return LoadAsync(tab, request, replace: false);
    }

    public void ResetForm(WebTab tab, WebField field)
    {
        lock (tab.Sync)
        {
            if (field.Form is { } form)
            {
                foreach (var other in form.Fields)
                {
                    other.Reset();
                }

                tab.Touch();
            }
        }
    }

    /// <summary>Checks a radio button (and unchecks the others of its group) or flips a checkbox.</summary>
    public void Toggle(WebTab tab, WebField field)
    {
        lock (tab.Sync)
        {
            if (field.Kind == WebFieldKind.Checkbox)
            {
                field.Checked = !field.Checked;
            }
            else if (field.Kind == WebFieldKind.Radio)
            {
                var group = field.Form?.Fields ?? tab.Current?.Document?.Fields ?? [];
                foreach (var other in group)
                {
                    if (other.Kind == WebFieldKind.Radio && other.Name == field.Name)
                    {
                        other.Checked = false;
                    }
                }

                field.Checked = true;
            }

            tab.Touch();
        }
    }

    public void CycleOption(WebTab tab, WebField field, int step)
    {
        lock (tab.Sync)
        {
            if (field.Options.Count > 0)
            {
                field.SelectedIndex = (field.SelectedIndex + step + field.Options.Count) % field.Options.Count;
                tab.Touch();
            }
        }
    }

    public void SetValue(WebTab tab, WebField field, string value)
    {
        lock (tab.Sync)
        {
            field.Value = value;
            tab.Touch();
        }
    }

    public WebField? FieldOf(WebTab tab, WebItem item)
    {
        lock (tab.Sync)
        {
            return item.IsField && tab.Current?.Document is { } document && item.Id < document.Fields.Count ? document.Fields[item.Id] : null;
        }
    }

    public void Say(WebTab tab, string message)
    {
        lock (tab.Sync)
        {
            tab.Message = message;
            tab.Touch();
        }
    }

    #endregion

    #region Layout

    /// <summary>
    /// Makes sure the page is laid out for <paramref name="width"/>: starts a background layout when it is
    /// not, and keeps showing the old one meanwhile. Called from <c>Draw</c>, so it only starts a task.
    /// </summary>
    public void EnsureLayout(WebTab tab, int width)
    {
        WebHistoryEntry entry;
        bool source;
        lock (tab.Sync)
        {
            if (tab.Current is not { } current || current.IsDropped || width <= 0)
            {
                return;
            }

            if (current.Page is { } page && page.Width == width && current.PageIsSource == tab.ShowSource)
            {
                return;
            }

            if (tab.LayoutWidth == width && !tab.LayoutTask.IsCompleted)
            {
                return;
            }

            entry = current;
            source = tab.ShowSource;
            tab.LayoutWidth = width;
        }

        var task = Task.Run(() =>
        {
            WebPage page;
            lock (entry.LayoutLock)
            {
                page = BuildPage(entry, width, source);
            }

            lock (tab.Sync)
            {
                if (tab.Current != entry || tab.ShowSource != source)
                {
                    return;
                }

                entry.Page = page;
                entry.PageIsSource = source;
                if (tab.LayoutWidth == width)
                {
                    tab.LayoutWidth = 0;
                }

                if (tab.PendingFragment is not null)
                {
                    ApplyFragment(tab, 1);
                }
                else
                {
                    entry.Scroll = Math.Clamp(entry.Scroll, 0, Math.Max(0, page.Lines.Count - 1));
                }

                tab.Touch();
            }
        });
        lock (tab.Sync)
        {
            tab.LayoutTask = task;
        }
    }

    private static WebPage BuildPage(WebHistoryEntry entry, int width, bool source)
    {
        if (source && entry.Source is { } text)
        {
            return WebPage.FromText(text, width, WebStyle.Code);
        }

        if (entry.PlainText is { } plain)
        {
            return WebPage.FromText(plain, width);
        }

        if (entry.Document is { } document)
        {
            entry.Layout ??= new HtmlLayout(document);
            return entry.Layout.Layout(width);
        }

        return WebPage.FromText(string.Empty, width);
    }

    /// <summary>Scrolls to the pending <c>#fragment</c> if the page has that anchor. Caller holds the tab's lock.</summary>
    private static void ApplyFragment(WebTab tab, int viewHeight)
    {
        if (tab.Current is not { Page: { } page } entry || tab.PendingFragment is not { } fragment)
        {
            return;
        }

        tab.PendingFragment = null;
        if (fragment.Length == 0)
        {
            entry.Scroll = 0;
        }
        else if (page.Anchors.TryGetValue(fragment, out var line))
        {
            entry.Scroll = Math.Clamp(line, 0, Math.Max(0, page.Lines.Count - viewHeight));
        }
    }

    #endregion

    #region Saving

    /// <summary>Writes the current page's content to the download folder and returns the path. Existing files are never overwritten.</summary>
    public string Save(WebTab tab)
    {
        WebFetchResponse? response;
        string title;
        lock (tab.Sync)
        {
            response = tab.Current?.Response;
            title = tab.Current?.Title ?? string.Empty;
        }

        if (response is null)
        {
            throw new InvalidOperationException("There is nothing to save on this page.");
        }

        Directory.CreateDirectory(downloadDirectory);
        var name = SafeFileName(response.FileName ?? LastSegment(response.Url));
        if (string.IsNullOrWhiteSpace(name) || name == "/" )
        {
            name = SafeFileName(title);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "page";
        }

        if (Path.GetExtension(name).Length == 0 && WebContent.Classify(response) == WebContentKind.Html)
        {
            name += ".html";
        }

        var path = Path.Combine(downloadDirectory, name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var i = 1; File.Exists(path); i++)
        {
            path = Path.Combine(downloadDirectory, $"{stem} ({i}){extension}");
        }

        File.WriteAllBytes(path, response.Content);
        return path;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        var result = builder.ToString().Trim(' ', '.');
        return result.Length > 120 ? result[..120] : result;
    }

    #endregion

    internal static string LastSegment(string url)
    {
        var path = WebAddress.WithoutFragment(url);
        var query = path.IndexOf('?');
        if (query >= 0)
        {
            path = path[..query];
        }

        var segment = path.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;
        return Uri.UnescapeDataString(segment.Length > 0 && !segment.EndsWith(':') ? segment : url);
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB",
    };
}

internal enum WebContentKind
{
    Html,
    Text,
    Binary,
}

/// <summary>Deciding what a response is, and turning its bytes into text with the right character set.</summary>
internal static class WebContent
{
    static WebContent()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static WebContentKind Classify(WebFetchResponse response)
    {
        var type = response.ContentType.Split(';')[0].Trim().ToLowerInvariant();
        if (type.Contains("html"))
        {
            return WebContentKind.Html;
        }

        if (type.Length == 0 || type == "application/octet-stream")
        {
            var head = Encoding.UTF8.GetString(response.Content, 0, Math.Min(response.Content.Length, 512)).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (head.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || head.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                return WebContentKind.Html;
            }

            if (type.Length == 0 && !head.Contains('\0'))
            {
                return WebContentKind.Text;
            }

            return WebContentKind.Binary;
        }

        if (type.StartsWith("text/", StringComparison.Ordinal)
            || type is "application/json" or "application/xml" or "application/javascript" or "application/x-javascript" or "application/yaml" or "application/x-yaml" or "application/x-sh"
            || type.EndsWith("+json", StringComparison.Ordinal) || type.EndsWith("+xml", StringComparison.Ordinal))
        {
            return WebContentKind.Text;
        }

        return WebContentKind.Binary;
    }

    public static string Decode(WebFetchResponse response)
    {
        var bytes = response.Content;
        var encoding = FromByteOrderMark(bytes, out var skip)
            ?? FromHeader(response.ContentType)
            ?? FromMeta(bytes);
        if (encoding is not null)
        {
            return encoding.GetString(bytes, skip, bytes.Length - skip);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    private static Encoding? FromByteOrderMark(byte[] bytes, out int skip)
    {
        skip = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            skip = 3;
            return Encoding.UTF8;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            skip = 2;
            return Encoding.Unicode;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            skip = 2;
            return Encoding.BigEndianUnicode;
        }

        return null;
    }

    private static Encoding? FromHeader(string contentType)
    {
        var index = contentType.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? null : Named(contentType[(index + 8)..].Split(';')[0].Trim().Trim('"', '\''));
    }

    private static Encoding? FromMeta(byte[] bytes)
    {
        var head = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 2048));
        var index = head.IndexOf("charset", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var start = index + 7;
        while (start < head.Length && (head[start] is ' ' or '=' or '"' or '\''))
        {
            start++;
        }

        var end = start;
        while (end < head.Length && (char.IsLetterOrDigit(head[end]) || head[end] is '-' or '_'))
        {
            end++;
        }

        return Named(head[start..end]);
    }

    private static Encoding? Named(string name)
    {
        try
        {
            return name.Length == 0 ? null : Encoding.GetEncoding(name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

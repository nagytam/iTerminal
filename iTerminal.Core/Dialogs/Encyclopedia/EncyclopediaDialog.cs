using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Encyclopedia;
using iTerminal.Core.Services.FileAssociations;

namespace iTerminal.Core.Dialogs.Encyclopedia;

/// <summary>
/// The encyclopedia: a term to type, a source to look it up in (Wikipedia in six languages, the Simple English Wikipedia,
/// Wiktionary, and Britannica as a link to its own search), the matching articles on the left and the one under the cursor
/// on the right. Enter searches; the cursor moving over the results reads each article's introduction; Enter on a result
/// or F3 reads the whole article; F2 opens it in the browser.
///
/// <para>Nothing here waits on the network: every request runs on a background task and parks its outcome until the next
/// idle pass applies it on the UI thread. Only the newest request may land, and closing cancels the one in flight.</para>
/// </summary>
public class EncyclopediaDialog : SplitPaneDialog
{
    internal const string KeyHints = "Enter:search/read  Tab:field  F3:whole  F2:browser  Esc:close";

    internal const string SourceKey = "Encyclopedia.Source";
    internal const string QueryKey = "Encyclopedia.Query";

    /// <summary>How long the cursor rests on a result before its article is fetched; scrolling through the list must not fetch every row.</summary>
    internal static int CursorDelayMilliseconds { get; set; } = 250;

    private enum OutcomeKind
    {
        Search,
        Article,
        Message,
    }

    private sealed record Outcome(int Generation, OutcomeKind Kind, IReadOnlyList<EncyclopediaHit>? Hits = null, EncyclopediaArticle? Article = null, string? Text = null, bool IsError = false);

    private sealed class PaneViewer(IThemeService themeService) : TextViewerControl(false, themeService)
    {
        public override void OnInitializeLayout()
        {
            Position placed = Position;
            base.OnInitializeLayout();
            Position = placed;
        }
    }

    private sealed class ResultListBox : ListBox<EncyclopediaHit>
    {
        public ResultListBox(IListBoxModel<EncyclopediaHit> listModel, IThemeService themeService)
            : base(listModel, themeService)
        {
            ListModel.TransformItem = (hit, _) => new ListItem<EncyclopediaHit>(hit) { Value = " " + hit.Title, IsEnabled = true };
        }
    }

    private readonly IEncyclopediaService _service;
    private readonly ISettingsService _settings;
    private readonly IFileLauncher _launcher;
    private readonly TextBox _query;
    private readonly ComboBox<EncyclopediaSource> _source;
    private readonly ConcurrentObservableCollection<EncyclopediaHit> _hits = new();
    private readonly ResultListBox _results;
    private readonly TextViewerControl _article;

    private CancellationTokenSource? _cancellation;
    private readonly Lock _landGate = new();
    private Outcome? _pending;
    private int _generation;
    private string _status = string.Empty;
    private bool _statusIsError;
    private bool _initializing;

    private EncyclopediaSource? _searchedSource;
    private string _articleTitle = string.Empty;
    private string _articleText = string.Empty;
    private string _articleUrl = string.Empty;
    private bool _articleIsFull;
    private int _requestedIndex = -1;
    private int _watchedIndex = -1;
    private long _watchedSince;

    public EncyclopediaDialog(
        IEncyclopediaService service,
        ISettingsService settings,
        IFileLauncher launcher,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(EncyclopediaDialog), themeService)
    {
        _service = service;
        _settings = settings;
        _launcher = launcher;
        Title = iConsole.Localization.UiText.Get("Menu.Encyclopedia", "Encyclopedia");

        _query = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("Encyclopedia.Term", "Term"),
            Placeholder = UiText.Get("Encyclopedia.TermPlaceholder", "a word or a name to look up"),
        };
        _source = new ComboBox<EncyclopediaSource>(themeService)
        {
            Label = UiText.Get("Encyclopedia.Source", "Source"),
            DisplaySelector = source => source.Name,
            MaxDropDownRows = 9,
        };
        _source.SetItems([.. service.Sources]);
        _source.SelectionChanged += (_, _) => OnSourceChanged();

        _results = new ResultListBox(new ListBoxModel<EncyclopediaHit>(_hits), themeService)
        {
            EmptyText = UiText.Get("Encyclopedia.EmptyResults", " Type a term above and press Enter."),
        };
        _article = new PaneViewer(themeService) { CanFocus = true };

        AddControl(_query);
        AddControl(_source);
        AddControl(_results);
        AddControl(_article);
    }

    #region Test seams

    internal TextBox QueryBox => _query;
    internal ComboBox<EncyclopediaSource> SourceBox => _source;
    internal ListBox<EncyclopediaHit> Results => _results;
    internal TextViewerControl Article => _article;
    internal string Status => _status;
    internal string ArticleText => _articleText;
    internal string ArticleTitle => _articleTitle;
    internal string ArticleUrl => _articleUrl;
    internal bool ArticleIsFull => _articleIsFull;

    /// <summary>The request the dialog last started; nothing awaits it in the dialog, so a test waits on it instead of polling.</summary>
    internal Task RequestTask { get; private set; } = Task.CompletedTask;

    /// <summary>Applies the pending outcome now; the idle pass does the same in the running app.</summary>
    internal void ApplyPendingForTest() => OnIdle();

    #endregion

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        _initializing = true;
        string? id = _settings.Get(SourceKey);
        _source.SelectedIndex = Math.Max(0, _source.Items.ToList().FindIndex(source => source.Id == id));
        _query.Text = _settings.Get(QueryKey) ?? string.Empty;
        _initializing = false;
    }

    public override void Close()
    {
        _settings.Set(SourceKey, SelectedSource.Id);
        _settings.Set(QueryKey, _query.Text);

        // Cancelled, not awaited: the request ends on its own, and a late outcome is dropped by its generation.
        _cancellation?.Cancel();
        NextGeneration();
        base.Close();
    }

    private EncyclopediaSource SelectedSource => _source.SelectedItem ?? EncyclopediaSources.All[0];

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.9);
        }

        int iw = (int)ViewSize.Width - 2;
        int ih = (int)ViewSize.Height - 2;
        if (iw > 0 && ih > 0)
        {
            // The source names are up to 24 cells long and the box needs room for its label and arrow as well.
            int queryWidth = Math.Max(12, Math.Min(iw / 2, iw - 36));
            _query.Position = new Position(1, 1);
            _query.ViewSize = new Size(queryWidth, 1);
            _query.ScrollableModel.ViewSize = _query.ViewSize;
            _source.Position = new Position(1 + queryWidth + 1, 1);
            _source.ViewSize = new Size(Math.Max(12, iw - queryWidth - 1), 1);

            int paneHeight = Math.Max(1, ih - 5);
            int leftWidth = Math.Clamp(iw / 3, Math.Min(16, iw), 40);
            int rightWidth = Math.Max(1, iw - leftWidth - 1);
            _results.Position = new Position(1, 4);
            _results.ViewSize = new Size(leftWidth, paneHeight);
            _article.Position = new Position(1 + leftWidth + 1, 4);
            _article.ViewSize = new Size(rightWidth, paneHeight);
        }

        base.OnInitializeLayout();
        ShowArticle();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", UiText.Get("DialogHint.SearchOrRead", "Search the term, or read the whole article under the cursor"));
        yield return ("F3", UiText.Get("DialogHint.WholeArticle", "Read the whole article, or only its introduction"));
        yield return ("F2", UiText.Get("DialogHint.OpenInBrowser", "Open the article in the browser"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift && !_source.IsOpen)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.Enter:
                    if (_query.IsFocused)
                    {
                        StartSearch();
                        return true;
                    }

                    if (_results.IsFocused)
                    {
                        ReadWhole();
                        return true;
                    }

                    break;
                case ConsoleKey.F3:
                    ReadWhole(toggle: true);
                    return true;
                case ConsoleKey.F2:
                    OpenInBrowser();
                    return true;
            }
        }

        bool handled = await base.HandleKeyEventAsync(keyEvent);
        StateHasChanged();
        return handled;
    }

    #region Actions

    private void OnSourceChanged()
    {
        // Moving to another source looks the same term up there, so the answer on screen is never from the one named above it.
        if (!_initializing && !string.IsNullOrWhiteSpace(_query.Text) && !ReferenceEquals(_searchedSource, SelectedSource))
        {
            StartSearch();
        }
    }

    /// <summary>Enter on the term: searches the chosen source on a background task.</summary>
    internal void StartSearch()
    {
        string term = _query.Text.Trim();
        if (term.Length == 0)
        {
            SetStatus(UiText.Get("Encyclopedia.TypeTerm", "Type a term to look up."), isError: true);
            return;
        }

        EncyclopediaSource source = SelectedSource;
        _searchedSource = source;
        int generation = BeginRequest(string.Format(UiText.Get("Encyclopedia.Searching", "Searching {0}…"), source.Name));
        CancellationToken token = _cancellation!.Token;

        RequestTask = Task.Run(async () =>
        {
            try
            {
                IReadOnlyList<EncyclopediaHit> hits = await _service.SearchAsync(source, term, token);
                Land(new Outcome(generation, OutcomeKind.Search, Hits: hits, Text: term));
            }
            catch (OperationCanceledException)
            {
                Land(new Outcome(generation, OutcomeKind.Message, Text: UiText.Get("Msg.Cancelled", "Cancelled.")));
            }
            catch (EncyclopediaException exception)
            {
                Land(new Outcome(generation, OutcomeKind.Message, Text: exception.Message, IsError: true));
            }
            catch (Exception exception)
            {
                Land(new Outcome(generation, OutcomeKind.Message, Text: string.Format(UiText.Get("Encyclopedia.SearchFailed", "The search failed: {0}"), exception.Message), IsError: true));
            }
        });
    }

    /// <summary>Fetches an article for the hit on a background task; the introduction unless the whole is wanted.</summary>
    internal void StartArticle(EncyclopediaHit hit, bool full)
    {
        EncyclopediaSource source = _searchedSource ?? SelectedSource;
        int generation = BeginRequest(string.Format(UiText.Get("Encyclopedia.Reading", "Reading {0}…"), hit.Title));
        CancellationToken token = _cancellation!.Token;

        RequestTask = Task.Run(async () =>
        {
            try
            {
                EncyclopediaArticle article = await _service.GetArticleAsync(source, hit.Title, full, token);
                Land(new Outcome(generation, OutcomeKind.Article, Article: article, Text: source.Name));
            }
            catch (OperationCanceledException)
            {
                Land(new Outcome(generation, OutcomeKind.Message, Text: UiText.Get("Msg.Cancelled", "Cancelled.")));
            }
            catch (EncyclopediaException exception)
            {
                Land(new Outcome(generation, OutcomeKind.Message, Text: exception.Message, IsError: true));
            }
            catch (Exception exception)
            {
                Land(new Outcome(generation, OutcomeKind.Message, Text: string.Format(UiText.Get("Encyclopedia.ReadFailed", "The article could not be read: {0}"), exception.Message), IsError: true));
            }
        });
    }

    /// <summary>Enter on a result, or F3: the whole article of the result under the cursor (F3 again goes back to the introduction).</summary>
    private void ReadWhole(bool toggle = false)
    {
        if (SelectedHit() is not { } hit)
        {
            SetStatus(UiText.Get("Encyclopedia.NoResultToRead", "There is no result to read yet. Search for a term first."), isError: true);
            return;
        }

        bool full = toggle ? !_articleIsFull || _articleTitle != hit.Title : true;
        _requestedIndex = SelectedIndex();
        StartArticle(hit, full);
    }

    /// <summary>F2: the article (or, for a link-out source, the search) in the system's browser.</summary>
    internal void OpenInBrowser()
    {
        string url = _articleUrl;
        if (url.Length == 0)
        {
            if (SelectedHit() is { } hit)
            {
                url = SelectedSource.PageUrl(hit.Title);
            }
            else if (!string.IsNullOrWhiteSpace(_query.Text) && SelectedSource.Kind == EncyclopediaKind.LinkOut)
            {
                url = SelectedSource.PageUrl(_query.Text.Trim());
            }
        }

        if (url.Length == 0)
        {
            SetStatus(UiText.Get("Encyclopedia.NothingToOpen", "There is no article to open yet."), isError: true);
            return;
        }

        int generation = _generation;
        RequestTask = Task.Run(async () =>
        {
            LaunchOutcome outcome = await _launcher.OpenUrlAsync(url);
            Land(new Outcome(generation, OutcomeKind.Message,
                Text: outcome.Started ? string.Format(UiText.Get("Encyclopedia.Opened", "Opened {0} in the browser."), url) : outcome.Failure ?? UiText.Get("Encyclopedia.OpenFailed", "The browser could not be started."),
                IsError: !outcome.Started));
        });
    }

    private int BeginRequest(string status)
    {
        // A request still running is replaced: cancelled, and its outcome dropped by the new generation.
        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();
        SetStatus(status, isError: false);
        return NextGeneration();
    }

    private int NextGeneration()
    {
        lock (_landGate)
        {
            return ++_generation;
        }
    }

    private void Land(Outcome outcome)
    {
        // A cancelled request still reports back. Only the newest request may park its outcome: a late "Cancelled." from
        // the one it replaced would otherwise overwrite the answer the newer one has just parked, and nothing would show it.
        lock (_landGate)
        {
            if (outcome.Generation == _generation)
            {
                _pending = outcome;
            }
        }
    }

    private int SelectedIndex() => (int)_results.ScrollableModel.CursorPosition.Y;

    private EncyclopediaHit? SelectedHit()
    {
        System.Collections.Generic.IReadOnlyList<IListItem<EncyclopediaHit>> items = _results.ListModel.ListItems;
        int index = SelectedIndex();
        return index >= 0 && index < items.Count ? items[index].Item : null;
    }

    public override void OnIdle()
    {
        base.OnIdle();

        Outcome? outcome = Interlocked.Exchange(ref _pending, null);
        if (outcome is not null && outcome.Generation == _generation)
        {
            Apply(outcome);
        }

        FollowCursor();
    }

    private void Apply(Outcome outcome)
    {
        switch (outcome.Kind)
        {
            case OutcomeKind.Search:
                IReadOnlyList<EncyclopediaHit> hits = outcome.Hits ?? [];
                _hits.ReplaceAll([.. hits]);
                _results.UpdateModel();
                _results.ScrollableModel.CursorPosition.Y = 0;
                _results.ScrollableModel.ScrollPosition.Y = 0;
                _requestedIndex = -1;
                _watchedIndex = -1;
                ClearArticle();
                if (hits.Count == 0)
                {
                    SetStatus(string.Format(UiText.Get("Encyclopedia.NothingFound", "Nothing found for \"{0}\"."), outcome.Text), isError: true);
                }
                else
                {
                    SetStatus(string.Format(UiText.Get("Encyclopedia.Found", "{0} found in {1}."), hits.Count, (_searchedSource ?? SelectedSource).Name), isError: false);
                    _requestedIndex = 0;
                    StartArticle(hits[0], full: false);
                }

                break;
            case OutcomeKind.Article when outcome.Article is { } article:
                _articleTitle = article.Title;
                _articleText = article.Text;
                _articleUrl = article.Url;
                _articleIsFull = article.IsFull;
                ShowArticle();
                SetStatus(
                    string.Format(
                        article.IsFull ? UiText.Get("Encyclopedia.WholeFrom", "{0} — the whole article") : UiText.Get("Encyclopedia.IntroFrom", "{0} — the introduction"),
                        outcome.Text),
                    isError: false);
                break;
            default:
                SetStatus(outcome.Text ?? string.Empty, outcome.IsError);
                break;
        }
    }

    /// <summary>
    /// Keeps the article in step with the cursor. Polled from here rather than driven by an event because the list box
    /// moves its cursor with its own scroll commands, which raise nothing this dialog could subscribe to; and only after
    /// the cursor has rested, so holding Down does not fetch every row it passes.
    /// </summary>
    private void FollowCursor()
    {
        int index = SelectedIndex();
        long now = Environment.TickCount64;
        if (index != _watchedIndex)
        {
            _watchedIndex = index;
            _watchedSince = now;
            return;
        }

        if (index != _requestedIndex && now - _watchedSince >= CursorDelayMilliseconds && SelectedHit() is { } hit)
        {
            _requestedIndex = index;
            StartArticle(hit, full: false);
        }
    }

    private void ClearArticle()
    {
        _articleTitle = string.Empty;
        _articleText = string.Empty;
        _articleUrl = string.Empty;
        _articleIsFull = false;
        ShowArticle();
    }

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
        StateHasChanged();
    }

    #endregion

    #region Presentation

    /// <summary>Puts the article in the right-hand pane, wrapped to the pane's width — the viewer only scrolls sideways otherwise.</summary>
    private void ShowArticle()
    {
        int width = Math.Max(8, (int)_article.ViewSize.Width - 2);
        List<string> lines = [];
        if (_articleTitle.Length > 0)
        {
            lines.Add(_articleTitle);
            lines.Add(new string('═', Math.Min(width, _articleTitle.Length)));
            lines.Add(string.Empty);
        }

        lines.AddRange(Translator.TranslatorDialog.Wrap(_articleText.Length == 0 && _articleTitle.Length > 0
            ? UiText.Get("Encyclopedia.NoText", "This article has no text of its own.")
            : _articleText, width));
        _article.TextModel.Lines = [.. lines];
        _article.ScrollableModel.ScrollableSize = new Size(lines.Count == 0 ? 0 : lines.Max(line => line.Length), lines.Count);
        _article.ScrollableModel.ScrollPosition.Y = 0;
        _article.ScrollableModel.ScrollPosition.X = 0;
        _article.ScrollableModel.CursorPosition.Y = 0;
        _article.ScrollableModel.CursorPosition.X = 0;
        StateHasChanged();
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int ix = (int)AbsolutePosition.X + 1;
        int iy = (int)AbsolutePosition.Y + 1;
        int iw = (int)ViewSize.Width - 2;
        int ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 0)
        {
            return;
        }

        WriteLine(buffer, ix, iy + 1, iw, string.Empty, colorScheme.Foreground, colorScheme.Background);
        int leftWidth = (int)_results.ViewSize.Width;
        int rightWidth = (int)_article.ViewSize.Width;
        DrawSectionHeader(buffer, ix, iy + 2, leftWidth, string.Format(UiText.Get("Encyclopedia.Results", "Results ({0})"), _hits.Count), _results.IsFocused, colorScheme);
        DrawSectionHeader(buffer, ix + leftWidth + 1, iy + 2, rightWidth, UiText.Get("Encyclopedia.Article", "Article"), _article.IsFocused, colorScheme);

        DrawStatusAndHints(buffer, ix, iy + ih - 1, iw, _status, _statusIsError ? colorScheme.Error : Color.Cyan, KeyHints, colorScheme);
    }

    #endregion
}

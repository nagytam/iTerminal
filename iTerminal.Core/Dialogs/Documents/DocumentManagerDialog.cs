using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Data.Documents;
using iTerminal.Core.Services.Documents;

namespace iTerminal.Core.Dialogs.Documents;

/// <summary>
/// Full-screen modal document manager: a search box, the results, and an optional preview of the
/// document under the cursor.
///
/// <para>The search box has focus from the moment the dialog opens and the results narrow as it is
/// typed. That is the whole interaction — there is no "press Enter to search" — because with the
/// text already extracted and indexed a query is a database index lookup, and making the user ask
/// for each one would hide how fast it is. Every query still runs on a background task and never on
/// the key handler that triggered it: the 200 ms budget is the budget whether or not the work
/// usually takes one.</para>
///
/// <para>The cursor keys work while the search box has focus, so narrowing a query and picking from
/// what is left is one continuous motion rather than a trip through Tab. Tab still moves focus, for
/// scrolling the preview.</para>
///
/// <para>With no query the list falls back to the whole index, newest first — the dialog is a way to
/// see what is indexed at all, not only a way to search it, and an empty screen would say nothing
/// about whether indexing has ever run.</para>
/// </summary>
public class DocumentManagerDialog : SplitPaneDialog
{
    /// <summary>
    /// How long typing has to pause before a search runs. Long enough that a fast typist causes one
    /// query instead of a dozen, short enough to feel immediate.
    /// </summary>
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(150);

    private const int MaxResults = 500;

    /// <summary>
    /// A row of the results list. The hit is carried whole so the row can show where the match was
    /// and what it looked like, which is the difference between a list of file names and a list of
    /// answers.
    /// </summary>
    private sealed class ResultListBox : ListBox<DocumentSearchHit>
    {
        public ResultListBox(IListBoxModel<DocumentSearchHit> listModel, IThemeService themeService)
            : base(listModel, themeService)
        {
            ListModel.TransformItem = Transform;
        }

        private ListItem<DocumentSearchHit> Transform(DocumentSearchHit hit, CancellationToken cancellationToken) =>
            new(hit) { Value = " " + FormatRow(hit), IsEnabled = true };

        private static string FormatRow(DocumentSearchHit hit)
        {
            DocumentEntry entry = hit.Entry;
            string folder = entry.RelativeFolder.Length > 0 ? $"  [{entry.RelativeFolder}]" : string.Empty;
            string detail = hit.Match == DocumentSearchMatch.Content && hit.Snippet.Length > 0
                ? $"  — {hit.Snippet}"
                : string.Empty;

            return $"{KindGlyph(entry.Kind)} {entry.DisplayName}{folder}{detail}";
        }

        /// <summary>
        /// A single letter per kind. Deliberately not an icon font: this draws into a terminal
        /// buffer whose cell width is one character, and a glyph the font renders double-wide would
        /// push every row out of alignment.
        /// </summary>
        private static char KindGlyph(DocumentKind kind) => kind switch
        {
            DocumentKind.Pdf => 'P',
            DocumentKind.Image => 'I',
            DocumentKind.Office => 'O',
            DocumentKind.Markup => 'M',
            DocumentKind.Text => 'T',
            _ => '·',
        };

        protected override Color? GetItemForegroundOverride(int index)
        {
            if (index < 0 || index >= ListModel.ListItems.Count)
            {
                return null;
            }

            DocumentEntry entry = ListModel.ListItems[index].Item.Entry;

            // A document with no text is still a valid result — it matched on its name — but it is
            // not one the preview will have anything to say about, and dimming it says so without
            // costing a column.
            return entry.TextLength > 0 ? null : Color.DarkGray;
        }
    }

    private sealed class PreviewListBox : ListBox<string>
    {
        public PreviewListBox(IListBoxModel<string> listModel, IThemeService themeService)
            : base(listModel, themeService)
        {
            ListModel.TransformItem = (line, ct) => new ListItem<string>(line) { Value = line, IsEnabled = true };
        }
    }

    private readonly IDocumentLibraryService _documentLibraryService;
    private readonly IContentModelService? _contentModelService;

    private readonly ConcurrentObservableCollection<DocumentSearchHit> _resultItems = new();
    private readonly ConcurrentObservableCollection<string> _previewItems = new();

    private readonly ResultListBox _resultListBox;
    private readonly PreviewListBox _previewListBox;

    /// <summary>
    /// Stands in for the preview while its text is being read, shaped like the paragraph that is coming.
    /// Shown and hidden from <see cref="OnIdle"/>: the read finishes on a worker thread, which must not touch
    /// the control tree.
    /// </summary>
    private readonly Skeleton _previewSkeleton;
    private readonly TextBox _searchBox;

    /// <summary>
    /// Bumped by every keystroke and every rescan. A search task compares it against the value it
    /// started with and discards its own results if it no longer matches, which is what stops a slow
    /// query for "in" from landing on top of the finished query for "invoice".
    /// </summary>
    private int _searchGeneration;

    private string _query = string.Empty;
    private bool _searching;
    private string _message = string.Empty;

    private bool _isPreviewVisible = true;

    /// <summary>The document the preview currently holds, so the poll below only reloads on a move.</summary>
    private string _previewPath = string.Empty;
    private string _previewText = string.Empty;
    private bool _previewLoading;
    private int _previewGeneration;

    /// <summary>Width the preview was last wrapped to; re-wrapped when the dialog is resized.</summary>
    private int _previewWrapWidth;

    private CancellationTokenSource? _rescanCts;

    public DocumentManagerDialog(
        IDocumentLibraryService documentLibraryService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService,
        IContentModelService? contentModelService = null)
        : base(nameof(DocumentManagerDialog), themeService)
    {
        _documentLibraryService = documentLibraryService;
        _contentModelService = contentModelService;
        Title = "Documents";

        _resultListBox = new ResultListBox(new ListBoxModel<DocumentSearchHit>(_resultItems), themeService);
        _previewListBox = new PreviewListBox(new ListBoxModel<string>(_previewItems), themeService);

        _searchBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = "Search",
            MaxLength = 512,
            Placeholder = "words to look for in your documents",
        };
        _searchBox.PropertyChanged += OnSearchTextChanged;

        // The search box is added first so the dialog's own layout pass — which focuses the first
        // focusable child — starts the user where they can type.
        AddControl(_searchBox);
        AddControl(_resultListBox);
        AddControl(_previewListBox);

        _previewSkeleton = new Skeleton(themeService, dateTimeProviderService) { IsVisible = false };
        AddControl(_previewSkeleton);
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        _query = string.Empty;
        _searchBox.Text = string.Empty;
        _message = string.Empty;
        _previewPath = string.Empty;
        _previewText = string.Empty;
        _previewItems.ReplaceAll([]);
        _rescanCts = null;

        _documentLibraryService.LibraryChanged += OnLibraryChanged;
        _documentLibraryService.IndexingProgressChanged += OnIndexingProgressChanged;

        StartSearch(string.Empty);
    }

    public override void Close()
    {
        _documentLibraryService.LibraryChanged -= OnLibraryChanged;
        _documentLibraryService.IndexingProgressChanged -= OnIndexingProgressChanged;

        // A rescan the user started is deliberately left running: it belongs to the index, which
        // outlives this dialog, and closing a window is not a reason to throw away minutes of
        // extraction. Only the subscriptions go — Ctrl+X is how a scan is actually stopped.
        base.Close();
    }

    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        // The listing the user is looking at was built from an index that has just changed under it.
        // A scan publishes in batches, so this arrives every few seconds while one runs — and the
        // query has not changed, so the cursor stays on the document the user was reading instead
        // of jumping back to the first row on every flush.
        StartSearch(_query, preserveSelection: true);
    }

    private void OnIndexingProgressChanged(object? sender, DocumentIndexingProgress e) => StateHasChanged();

    #region Search

    private void OnSearchTextChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        string text = _searchBox.Text ?? string.Empty;
        if (string.Equals(text, _query, StringComparison.Ordinal))
        {
            return;
        }

        _query = text;
        StartSearch(text);
    }

    /// <summary>
    /// Runs a query after the debounce, on a background task. Returns immediately — this is called
    /// from a key handler. <paramref name="preserveSelection"/> is for a refresh of the same query:
    /// a new query starts at the top, a refreshed one keeps the row under the cursor.
    /// </summary>
    private void StartSearch(string query, bool preserveSelection = false)
    {
        int generation = Interlocked.Increment(ref _searchGeneration);
        _searching = true;
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SearchDebounce);
                if (generation != Volatile.Read(ref _searchGeneration))
                {
                    // Superseded while waiting: the newer keystroke's task will do the work, and
                    // this one must not clear the "searching" flag out from under it.
                    return;
                }

                IReadOnlyList<DocumentSearchHit> hits = query.Trim().Length == 0
                    ? BrowseAll()
                    : await _documentLibraryService.SearchAsync(query, MaxResults);

                if (generation != Volatile.Read(ref _searchGeneration))
                {
                    return;
                }

                // The results first, then "done": cleared the other way round there was a moment that
                // read as finished while the old list (and the old cursor) was still in place.
                _message = DescribeResults(query, hits.Count);
                ApplyResults(hits, preserveSelection);
                _searching = false;
            }
            catch (Exception exception)
            {
                if (generation == Volatile.Read(ref _searchGeneration))
                {
                    _searching = false;
                    _message = $"Search failed: {exception.Message}";
                }
            }
            finally
            {
                StateHasChanged();
            }
        });
    }

    /// <summary>
    /// The no-query listing: everything indexed, newest first, wrapped in hits so the results list
    /// has one row type rather than two.
    /// </summary>
    private IReadOnlyList<DocumentSearchHit> BrowseAll()
    {
        IReadOnlyList<DocumentEntry> entries = _documentLibraryService.Entries;
        List<DocumentSearchHit> hits = new(Math.Min(entries.Count, MaxResults));

        for (int index = 0; index < entries.Count && index < MaxResults; index++)
        {
            hits.Add(new DocumentSearchHit(entries[index], DocumentSearchMatch.Name, string.Empty, index));
        }

        return hits;
    }

    private string DescribeResults(string query, int count)
    {
        if (query.Trim().Length == 0)
        {
            int total = _documentLibraryService.DocumentCount;
            return total == 0
                ? "Nothing indexed yet — Ctrl+R scans the documents folder."
                : $"{total} document{(total == 1 ? string.Empty : "s")} indexed, "
                  + $"{_documentLibraryService.IndexedTextCount} with searchable text.";
        }

        return count == 0
            ? $"No document matches \"{query.Trim()}\"."
            : $"{count}{(count >= MaxResults ? "+" : string.Empty)} match{(count == 1 ? string.Empty : "es")}.";
    }

    private void ApplyResults(IReadOnlyList<DocumentSearchHit> hits, bool preserveSelection = false)
    {
        string selectedPath = SelectedHit()?.Entry.Path ?? string.Empty;
        ScrollableModel scrollableModel = _resultListBox.ScrollableModel;
        int oldCursor = (int)scrollableModel.CursorPosition.Y;
        int oldScroll = (int)scrollableModel.ScrollPosition.Y;

        _resultItems.ReplaceAll([.. hits]);
        _resultListBox.UpdateModel();

        int cursor = 0;
        if (preserveSelection && hits.Count > 0)
        {
            int index = -1;
            if (selectedPath.Length > 0)
            {
                for (int i = 0; i < hits.Count; i++)
                {
                    if (string.Equals(hits[i].Entry.Path, selectedPath, StringComparison.Ordinal))
                    {
                        index = i;
                        break;
                    }
                }
            }

            // The document itself when it is still listed, otherwise the same row number — the
            // neighbourhood the user was looking at, rather than the top of the list.
            cursor = index >= 0 ? index : Math.Clamp(oldCursor, 0, hits.Count - 1);
        }

        int rows = Math.Max(1, (int)_resultListBox.ViewSize.Height);
        int scroll = preserveSelection ? oldScroll : 0;
        if (cursor < scroll)
        {
            scroll = cursor;
        }
        else if (cursor >= scroll + rows)
        {
            scroll = cursor - rows + 1;
        }

        scrollableModel.CursorPosition.Y = cursor;
        scrollableModel.ScrollPosition.Y = Math.Max(0, scroll);

        // The old preview may belong to a document that is no longer in the list; clearing the
        // remembered path is what makes the idle poll reload the row now under the cursor. A
        // refresh that kept the same document keeps its preview (and where it was scrolled to).
        if (!preserveSelection || !string.Equals(SelectedHit()?.Entry.Path ?? string.Empty, selectedPath, StringComparison.Ordinal))
        {
            _previewPath = string.Empty;
        }
    }

    /// <summary>True while a scan this dialog started is running — the only kind Ctrl+X can stop.</summary>
    private bool OwnsRunningScan =>
        _documentLibraryService.IsIndexing && _rescanCts is { IsCancellationRequested: false };

    #endregion

    #region Preview

    private DocumentSearchHit? SelectedHit()
    {
        System.Collections.Generic.IReadOnlyList<IListItem<DocumentSearchHit>> items = _resultListBox.ListModel.ListItems;
        int index = (int)_resultListBox.ScrollableModel.CursorPosition.Y;
        return index >= 0 && index < items.Count ? items[index].Item : null;
    }

    /// <summary>
    /// Keeps the preview in step with the cursor. Polled from <see cref="OnIdle"/> rather than
    /// driven by an event because the cursor is moved by the list box's own scroll commands, which
    /// raise nothing this dialog could subscribe to — the same reason the music viewer polls the
    /// player's position.
    /// </summary>
    private void SyncPreview()
    {
        if (!_isPreviewVisible)
        {
            return;
        }

        DocumentSearchHit? hit = SelectedHit();
        string path = hit?.Entry.Path ?? string.Empty;

        if (string.Equals(path, _previewPath, StringComparison.Ordinal))
        {
            return;
        }

        _previewPath = path;
        _previewText = string.Empty;
        _previewItems.ReplaceAll([]);
        _previewWrapWidth = 0;

        if (path.Length == 0)
        {
            return;
        }

        if (hit is not null && hit.Entry.TextLength == 0)
        {
            // Nothing was extracted, and the row knows why. Saying so beats a blank pane the user
            // would read as "still loading".
            _previewText = DescribeMissingText(hit.Entry);
            WrapPreview(force: true);
            return;
        }

        _previewLoading = true;
        int generation = Interlocked.Increment(ref _previewGeneration);

        _ = Task.Run(async () =>
        {
            try
            {
                string text = await _documentLibraryService.GetTextAsync(path);
                if (generation != Volatile.Read(ref _previewGeneration))
                {
                    return;
                }

                _previewText = text;
                _previewLoading = false;
                WrapPreview(force: true);
            }
            catch (Exception exception)
            {
                if (generation == Volatile.Read(ref _previewGeneration))
                {
                    _previewLoading = false;
                    _previewText = $"Could not read the cached text: {exception.Message}";
                    WrapPreview(force: true);
                }
            }
            finally
            {
                StateHasChanged();
            }
        });
    }

    private static string DescribeMissingText(DocumentEntry entry)
    {
        string reason = entry.ExtractionNote.Length > 0
            ? entry.ExtractionNote
            : entry.ExtractionStatus switch
            {
                DocumentExtractionStatus.Unsupported => "no extractor handles this file type",
                DocumentExtractionStatus.TooLarge => "the file is over the extraction size limit",
                DocumentExtractionStatus.Failed => "text extraction failed",
                DocumentExtractionStatus.NotAttempted => "not extracted yet",
                _ => "no text was found",
            };

        return $"No preview: {reason}.";
    }

    /// <summary>
    /// Re-flows the preview text to the pane width. Wrapping is redone only when the width actually
    /// changed, since this is reached from the idle pass and the text can be a million characters.
    /// </summary>
    private void WrapPreview(bool force = false)
    {
        int width = (int)_previewListBox.ViewSize.Width;
        if (width <= 0)
        {
            return;
        }

        if (!force && width == _previewWrapWidth)
        {
            return;
        }

        _previewWrapWidth = width;
        _previewItems.ReplaceAll(WrapLines(_previewText, width));
        _previewListBox.UpdateModel();
        _previewListBox.ScrollableModel.CursorPosition.Y = 0;
        _previewListBox.ScrollableModel.ScrollPosition.Y = 0;
    }

    /// <summary>
    /// Breaks text into display lines of at most <paramref name="width"/> characters, at word
    /// boundaries where there is one and mid-word where there is not — a base64 blob in a document
    /// must not produce one line a thousand characters long that the pane then clips to nothing.
    /// </summary>
    internal static List<string> WrapLines(string text, int width) => text.WrapWords(width);

    private void TogglePreview()
    {
        _isPreviewVisible = !_isPreviewVisible;
        _previewListBox.IsVisible = _isPreviewVisible;
        _previewListBox.CanFocus = _isPreviewVisible;

        if (!_isPreviewVisible && _previewListBox.IsFocused)
        {
            _resultListBox.SetFocus();
        }

        OnInitializeLayout();
        StateHasChanged();
    }

    #endregion

    #region Actions

    /// <summary>
    /// Starts a rescan on a background task. The scan itself belongs to the service — the same call
    /// the indexing job makes — so what happens here is only that the user does not have to wait an
    /// hour for the periodic pass.
    /// </summary>
    private void StartRescan()
    {
        if (_documentLibraryService.IsIndexing)
        {
            return;
        }

        CancellationTokenSource cts = new();
        _rescanCts = cts;
        _message = "Scanning…";
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            try
            {
                await _documentLibraryService.RefreshAsync(cts.Token);
                _message = _documentLibraryService.StatusMessage;
            }
            catch (OperationCanceledException)
            {
                _message = "Scan cancelled.";
            }
            catch (Exception exception)
            {
                _message = $"Scan failed: {exception.Message}";
            }
            finally
            {
                StartSearch(_query, preserveSelection: true);
            }
        });
    }

    private void CancelRescan()
    {
        if (_rescanCts is { IsCancellationRequested: false })
        {
            _rescanCts.Cancel();
            _message = "Cancelling…";
            StateHasChanged();
        }
        else if (_documentLibraryService.IsIndexing)
        {
            // The periodic indexer's scan is not this dialog's to stop; saying so beats a key that
            // silently does nothing.
            _message = "The background indexer is scanning; it cannot be stopped from here.";
            StateHasChanged();
        }
    }

    /// <summary>
    /// Points the focused file panel at the selected document's folder and closes the dialog. The
    /// reason the dialog does this rather than opening a viewer of its own: this is a file manager,
    /// and "I found it, now show me where it is" is what the rest of the app is for.
    /// </summary>
    private async Task RevealSelectedAsync()
    {
        DocumentSearchHit? hit = SelectedHit();
        if (hit is null || _contentModelService is null)
        {
            return;
        }

        string? folder = Path.GetDirectoryName(hit.Entry.Path);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        Close();
        await _contentModelService.SetUrlAsync(new Url(folder + Path.DirectorySeparatorChar));
    }

    #endregion

    #region Input

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Type", "Search the documents");
        yield return ("Up/Down", "Choose a result");
        yield return ("Enter", "Show the document in the panel");
        yield return ("Esc", "Clear the search, then close");
        yield return ("Ctrl+R", "Scan the documents again");
        yield return ("Ctrl+X", "Cancel the scan");
        yield return ("Ctrl+T", "Show or hide the preview");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape when _query.Length > 0:
                // The first Escape clears the query, the second closes. Anything else would make a
                // mistyped search a reason to reopen the dialog.
                _searchBox.Text = string.Empty;
                _searchBox.SetFocus();
                return true;

            case ConsoleKey.Enter:
                await RevealSelectedAsync();
                return true;

            case ConsoleKey.R when keyEvent.Control:
                StartRescan();
                return true;

            case ConsoleKey.X when keyEvent.Control:
                CancelRescan();
                return true;

            case ConsoleKey.T when keyEvent.Control:
                TogglePreview();
                return true;
        }

        // The cursor keys drive the results list wherever focus happens to be, so narrowing a query
        // and choosing from what is left needs no trip through Tab. Only while the search box has
        // focus: once focus is in the preview, they have to scroll the preview.
        if (_searchBox.IsFocused && IsResultNavigationKey(keyEvent))
        {
            return await _resultListBox.HandleKeyEventAsync(keyEvent);
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private static bool IsResultNavigationKey(KeyEvent keyEvent) =>
        keyEvent.ConsoleKey is ConsoleKey.UpArrow or ConsoleKey.DownArrow
            or ConsoleKey.PageUp or ConsoleKey.PageDown;

    public override void OnIdle()
    {
        SyncPreview();
        WrapPreview();
        _previewSkeleton.IsVisible = _previewLoading && _isPreviewVisible && !_previewSkeleton.ViewSize.IsEmpty;
        base.OnIdle();
    }

    #endregion

    #region Test seams

    internal string? SelectedPathForTest => SelectedHit()?.Entry.Path;
    internal int ResultCountForTest => _resultListBox.ListModel.ListItems.Count;
    internal bool IsPreviewSkeletonVisibleForTest => _previewSkeleton.IsVisible;
    internal bool IsSearchingForTest => _searching;
    internal string MessageForTest => _message;
    internal IReadOnlyList<string> PreviewLinesForTest => [.. _previewItems.AsSnapshot()];
    internal ListBox<DocumentSearchHit> ResultListBoxForTest => _resultListBox;
    internal TextBox SearchBoxForTest => _searchBox;

    #endregion

    #region Layout and drawing

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.95);
        }

        int innerWidth = (int)ViewSize.Width - 2;
        int innerHeight = (int)ViewSize.Height - 2;

        if (innerWidth > 0 && innerHeight > 0)
        {
            // Row 0 is the search box, row 1 the status line, row 2 the section headers, and the
            // last row the footer; the lists get what is between.
            int listTop = 3;
            int listHeight = Math.Max(0, innerHeight - listTop - 1);

            _searchBox.Position = new Position(1, 1);
            _searchBox.ViewSize = new Size(innerWidth, 1);
            _searchBox.ScrollableModel.ViewSize = _searchBox.ViewSize;

            int resultWidth = _isPreviewVisible ? Math.Max(24, innerWidth * 45 / 100) : innerWidth;

            _resultListBox.Position = new Position(1, listTop + 1);
            _resultListBox.ViewSize = new Size(resultWidth, listHeight);

            if (_isPreviewVisible)
            {
                int previewX = 1 + resultWidth + 1;
                int previewWidth = innerWidth - resultWidth - 1;
                _previewListBox.Position = new Position(previewX, listTop + 1);
                _previewListBox.ViewSize = previewWidth > 0
                    ? new Size(previewWidth, listHeight)
                    : new Size(0, 0);
            }
            else
            {
                _previewListBox.ViewSize = new Size(0, 0);
            }

            _previewSkeleton.Position = _previewListBox.Position;
            _previewSkeleton.ViewSize = _previewListBox.ViewSize;
        }

        // Children must have their Position/ViewSize assigned before base.OnInitializeLayout()
        // recurses into them: each control sizes its own ScrollableModel from its own ViewSize.
        base.OnInitializeLayout();
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int x = (int)AbsolutePosition.X + 1;
        int y = (int)AbsolutePosition.Y + 1;
        int innerWidth = (int)ViewSize.Width - 2;
        int innerHeight = (int)ViewSize.Height - 2;

        if (innerWidth <= 0 || innerHeight <= 0)
        {
            return;
        }

        DrawStatus(buffer, x, y + 1, innerWidth, colorScheme);
        DrawFooter(buffer, x, y + innerHeight - 1, innerWidth, colorScheme);

        // Inner row 2, directly above the lists (which start on inner row 3). It was drawn on row 3,
        // the lists' own first row, so each list painted over its header and neither "Results (n)"
        // nor the preview's page/word/size line was ever seen.
        int headerY = y + 2;
        int resultWidth = _isPreviewVisible ? Math.Max(24, innerWidth * 45 / 100) : innerWidth;

        DrawSectionHeader(
            buffer,
            x,
            headerY,
            resultWidth,
            $"Results ({_resultListBox.Count})",
            _resultListBox.IsFocused || _searchBox.IsFocused,
            colorScheme);

        if (_isPreviewVisible)
        {
            int previewX = x + resultWidth + 1;
            int previewWidth = innerWidth - resultWidth - 1;
            if (previewWidth > 0)
            {
                DrawSectionHeader(
                    buffer,
                    previewX,
                    headerY,
                    previewWidth,
                    DescribePreviewHeader(),
                    _previewListBox.IsFocused,
                    colorScheme);
            }
        }
    }

    /// <summary>
    /// The line between the search box and the lists: what a scan is doing if one is running,
    /// otherwise how the last search went. Indexing wins because it is the only one of the two the
    /// user cannot see the result of anywhere else.
    /// </summary>
    private void DrawStatus(IBuffer buffer, int x, int y, int width, ColorScheme colorScheme)
    {
        DocumentIndexingProgress progress = _documentLibraryService.Progress;

        string text;
        Color foreground;

        if (progress.IsRunning)
        {
            text = progress.Describe();
            foreground = Color.Yellow;
        }
        else if (_searching)
        {
            text = "Searching…";
            foreground = colorScheme.Foreground;
        }
        else
        {
            text = _message;
            foreground = _message.Contains("failed", StringComparison.OrdinalIgnoreCase)
                ? colorScheme.Error
                : colorScheme.Foreground;
        }

        WriteLine(buffer, x, y, width, text, foreground, colorScheme.Background);
    }

    private string DescribePreviewHeader()
    {
        DocumentSearchHit? hit = SelectedHit();
        if (hit is null)
        {
            return "Preview";
        }

        DocumentEntry entry = hit.Entry;
        List<string> parts = [];

        if (entry.PageCount > 0)
        {
            parts.Add($"{entry.PageCount} page{(entry.PageCount == 1 ? string.Empty : "s")}");
        }

        if (entry.WordCount > 0)
        {
            parts.Add($"{entry.WordCount} words");
        }

        if (entry.Author.Length > 0)
        {
            parts.Add(entry.Author);
        }

        parts.Add(FormatSize(entry.Size));

        string detail = string.Join(" · ", parts);
        return _previewLoading ? $"Preview — loading… ({detail})" : $"Preview — {detail}";
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB",
    };

    private void DrawFooter(IBuffer buffer, int x, int y, int width, ColorScheme colorScheme)
    {
        // Only a scan this dialog started can be stopped from it; advertising Ctrl+X over the
        // periodic indexer's scan offered a key that did nothing.
        string scanKey = OwnsRunningScan
            ? "Ctrl+X:stop scan"
            : _documentLibraryService.IsIndexing ? "scanning…" : "Ctrl+R:rescan";
        string escKey = _query.Length > 0 ? "Esc:clear" : "Esc:close";
        string keys = $"Enter:reveal  Ctrl+T:preview  {scanKey}  Tab:focus  {escKey}";

        // The capability line is on the same row on purpose: whether search is indexed and whether
        // OCR ran are the two things that explain a result the user did not expect, and a row of
        // their own would cost a line of the results list on every screen to say it.
        // The keys come first and are never cut mid-hint; the capability line takes what is left,
        // and is left out rather than shown as a stub ("│ Sear") when there is no room for it.
        string capability = _documentLibraryService.CapabilityMessage ?? string.Empty;
        string fittedKeys = keys.FitHints(width);
        string text = capability.Length > 0 && fittedKeys.Length + 5 + Math.Min(capability.Length, 12) <= width
            ? $"{fittedKeys}   │ {capability}"
            : fittedKeys;
        WriteLine(buffer, x, y, width, text, colorScheme.Foreground, colorScheme.Background);
    }

    #endregion
}

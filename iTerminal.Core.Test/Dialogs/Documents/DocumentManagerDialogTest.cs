using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Data.Documents;
using iTerminal.Core.Dialogs.Documents;
using iTerminal.Core.Services.Documents;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Documents;

/// <summary>
/// The Alt+F3 document manager. Search runs on a background task behind a debounce, so every test
/// here waits for the dialog to settle rather than asserting straight after a key.
/// </summary>
[TestClass]
public class DocumentManagerDialogTest
{
    private sealed class FakeDocumentLibraryService : IDocumentLibraryService
    {
        public List<DocumentEntry> Documents { get; } = [];
        public void RequestIndexing() { }
        public Dictionary<string, string> Texts { get; } = [];
        private readonly List<string> _queries = [];

        /// <summary>A snapshot: searches run on background tasks.</summary>
        public List<string> Queries
        {
            get
            {
                lock (_queries)
                {
                    return [.. _queries];
                }
            }
        }
        public bool IsIndexing { get; set; }
        public DocumentIndexingProgress Progress { get; set; } = DocumentIndexingProgress.Idle;
        public TaskCompletionSource? RefreshGate { get; set; }
        public int RefreshCalls { get; private set; }
        public Exception? SearchFailure { get; set; }

        public string DocumentsFolder => "/docs";
        public int DocumentCount => Documents.Count;
        public int IndexedTextCount => Documents.Count(d => d.TextLength > 0);
        public DateTimeOffset? LastIndexedUtc => null;
        public string StatusMessage => "Indexed.";
        public string CapabilityMessage => "FTS5";
        public IReadOnlyList<DocumentEntry> Entries => Documents;

        public event EventHandler? LibraryChanged;
        public event EventHandler<DocumentIndexingProgress>? IndexingProgressChanged;

        public void RaiseLibraryChanged() => LibraryChanged?.Invoke(this, EventArgs.Empty);
        public void RaiseProgress() => IndexingProgressChanged?.Invoke(this, Progress);

        public void Preload()
        {
        }

        public async Task<int> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            IsIndexing = true;
            try
            {
                if (RefreshGate is not null)
                {
                    await RefreshGate.Task.WaitAsync(cancellationToken);
                }

                return Documents.Count;
            }
            finally
            {
                IsIndexing = false;
            }
        }

        public Task<IReadOnlyList<DocumentSearchHit>> SearchAsync(string query, int limit = 200, CancellationToken cancellationToken = default)
        {
            lock (_queries)
            {
                _queries.Add(query);
            }
            if (SearchFailure is not null)
            {
                throw SearchFailure;
            }

            IReadOnlyList<DocumentSearchHit> hits = [.. Documents
                .Where(d => d.FileName.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select((d, i) => new DocumentSearchHit(d, DocumentSearchMatch.Name, string.Empty, i))];
            return Task.FromResult(hits);
        }

        /// <summary>When set, the text is not returned until the gate opens: a preview that is still loading.</summary>
        public TaskCompletionSource? TextGate { get; set; }

        public async Task<string> GetTextAsync(string path, CancellationToken cancellationToken = default)
        {
            if (TextGate is not null)
            {
                await TextGate.Task;
            }

            return Texts.TryGetValue(path, out var text) ? text : string.Empty;
        }
    }

    private static DocumentEntry Doc(string name, int textLength = 10, string note = "", DocumentExtractionStatus status = DocumentExtractionStatus.Extracted) => new()
    {
        Path = Path.Combine(Path.GetTempPath(), "docs", name),
        FileName = name,
        Kind = DocumentKind.Text,
        TextLength = textLength,
        ExtractionNote = note,
        ExtractionStatus = status,
        Size = 2048,
    };

    private static DocumentManagerDialog CreateDialog(FakeDocumentLibraryService service, IContentModelService? contentModelService = null)
    {
        var dateTime = new Mock<IDateTimeProviderService>();
        dateTime.SetupGet(d => d.Now).Returns(DateTime.Now);
        return new DocumentManagerDialog(service, new Mock<IBeepService>().Object, dateTime.Object, TestTheme(), contentModelService);
    }

    private static async Task SettleAsync(DocumentManagerDialog dialog, int expectedRows)
    {
        await WaitUntilAsync(() => !dialog.IsSearchingForTest && dialog.ResultCountForTest == expectedRows,
            $"the result list to settle on {expectedRows} rows (has {dialog.ResultCountForTest})");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpeningWithNoQueryListsEverythingIndexed()
    {
        var service = new FakeDocumentLibraryService();
        service.Documents.AddRange([Doc("a.txt"), Doc("b.txt"), Doc("c.txt")]);
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);

        await SettleAsync(dialog, 3);

        var screen = Render(dialog);
        AssertScreenContains(screen, "3 documents indexed, 3 with searchable text.");
        AssertScreenContains(screen, "Results (3)");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptyIndexSaysHowToFillIt()
    {
        var dialog = CreateDialog(new FakeDocumentLibraryService());
        await ShowAsync(dialog);

        await WaitUntilAsync(() => dialog.MessageForTest.Length > 0, "the empty-index message");

        StringAssert.Contains(dialog.MessageForTest, "Ctrl+R");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TypingNarrowsTheResults()
    {
        var service = new FakeDocumentLibraryService();
        service.Documents.AddRange([Doc("invoice.pdf"), Doc("letter.txt"), Doc("invoice-2.pdf")]);
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 3);

        await TypeAsync(dialog, "invoice");

        await SettleAsync(dialog, 2);
        Assert.AreEqual("2 matches.", dialog.MessageForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ANoMatchQuerySaysSo()
    {
        var service = new FakeDocumentLibraryService();
        service.Documents.Add(Doc("a.txt"));
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 1);

        await TypeAsync(dialog, "zzz");

        await SettleAsync(dialog, 0);
        Assert.AreEqual("No document matches \"zzz\".", dialog.MessageForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailingSearchIsReportedInTheStatusLine()
    {
        var service = new FakeDocumentLibraryService { SearchFailure = new InvalidOperationException("database locked") };
        service.Documents.Add(Doc("a.txt"));
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 1);

        await TypeAsync(dialog, "a");

        await WaitUntilAsync(() => dialog.MessageForTest.StartsWith("Search failed", StringComparison.Ordinal), "the failure message");
        StringAssert.Contains(dialog.MessageForTest, "database locked");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task FirstEscapeClearsTheQuerySecondCloses()
    {
        var service = new FakeDocumentLibraryService();
        service.Documents.AddRange([Doc("a.txt"), Doc("b.txt")]);
        var dialog = CreateDialog(service);
        var root = await ShowAsync(dialog);
        await SettleAsync(dialog, 2);
        await TypeAsync(dialog, "a");
        await SettleAsync(dialog, 1);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.AreEqual(string.Empty, dialog.SearchBoxForTest.Text);
        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog), "the first Esc only clears the query");
        await SettleAsync(dialog, 2);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog), "the second Esc closes the dialog");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheFooterNamesEscapeForWhatItWillDo()
    {
        var service = new FakeDocumentLibraryService();
        service.Documents.Add(Doc("a.txt"));
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 1);

        AssertScreenContains(Render(dialog), "Esc:close");

        await TypeAsync(dialog, "a");

        AssertScreenContains(Render(dialog), "Esc:clear");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ArrowKeysMoveTheResultCursorWhileTheSearchBoxHasFocus()
    {
        var service = new FakeDocumentLibraryService();
        service.Documents.AddRange([Doc("a.txt"), Doc("b.txt"), Doc("c.txt")]);
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 3);
        Assert.IsTrue(dialog.SearchBoxForTest.IsFocused, "the search box has focus from the start");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));

        Assert.AreEqual(service.Documents[1].Path, dialog.SelectedPathForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnIndexRefreshKeepsTheCursorOnTheSameDocument()
    {
        // A scan publishes every few seconds; each flush used to throw the cursor back to row 0.
        var service = new FakeDocumentLibraryService();
        service.Documents.AddRange([Doc("a.txt"), Doc("b.txt"), Doc("c.txt")]);
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 3);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(service.Documents[2].Path, dialog.SelectedPathForTest);

        // A new document lands at the top of the newest-first listing.
        service.Documents.Insert(0, Doc("new.txt"));
        service.RaiseLibraryChanged();

        await SettleAsync(dialog, 4);
        Assert.AreEqual(service.Documents[3].Path, dialog.SelectedPathForTest, "the cursor follows c.txt to its new row");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ANewQueryStartsAtTheTop()
    {
        var service = new FakeDocumentLibraryService();
        service.Documents.AddRange([Doc("aa.txt"), Doc("ab.txt"), Doc("ac.txt")]);
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 3);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));

        await TypeAsync(dialog, "a");

        await WaitUntilAsync(() => service.Queries.Contains("a") && !dialog.IsSearchingForTest, "the search to run");
        Assert.AreEqual(service.Documents[0].Path, dialog.SelectedPathForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ThePreviewShowsTheSelectedDocumentsText()
    {
        var service = new FakeDocumentLibraryService();
        var doc = Doc("a.txt");
        service.Documents.Add(doc);
        service.Texts[doc.Path] = "Hello preview";
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 1);

        await WaitUntilAsync(() =>
        {
            dialog.OnIdle();
            return dialog.PreviewLinesForTest.Contains("Hello preview");
        }, "the preview to load");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASkeletonStandsInForThePreviewWhileItsTextLoads()
    {
        var service = new FakeDocumentLibraryService { TextGate = new TaskCompletionSource() };
        var doc = Doc("a.txt");
        service.Documents.Add(doc);
        service.Texts[doc.Path] = "Hello preview";
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 1);

        await WaitUntilAsync(() =>
        {
            dialog.OnIdle();
            return dialog.IsPreviewSkeletonVisibleForTest;
        }, "the skeleton to show while the text loads");

        service.TextGate.SetResult();

        await WaitUntilAsync(() =>
        {
            dialog.OnIdle();
            return !dialog.IsPreviewSkeletonVisibleForTest && dialog.PreviewLinesForTest.Contains("Hello preview");
        }, "the skeleton to give way to the text");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADocumentWithoutTextExplainsWhyThereIsNoPreview()
    {
        var service = new FakeDocumentLibraryService();
        service.Documents.Add(Doc("scan.pdf", textLength: 0, status: DocumentExtractionStatus.Empty, note: "scanned image, needs OCR"));
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 1);

        dialog.OnIdle();

        Assert.IsTrue(dialog.PreviewLinesForTest.Any(l => l.Contains("No preview: scanned image, needs OCR.")),
            string.Join(Environment.NewLine, dialog.PreviewLinesForTest));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlTHidesAndShowsThePreview()
    {
        var service = new FakeDocumentLibraryService();
        service.Documents.Add(Doc("a.txt"));
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 1);
        AssertScreenContains(Render(dialog), "Preview");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.T, control: true));

        AssertScreenDoesNotContain(Render(dialog), "Preview —");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.T, control: true));

        AssertScreenContains(Render(dialog), "Preview");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlRStartsARescanThatCtrlXCanStop()
    {
        var service = new FakeDocumentLibraryService { RefreshGate = new TaskCompletionSource() };
        service.Documents.Add(Doc("a.txt"));
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 1);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.R, control: true));
        await WaitUntilAsync(() => service.IsIndexing, "the rescan to start");

        AssertScreenContains(Render(dialog), "Ctrl+X:stop scan");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.X, control: true));

        await WaitUntilAsync(() => dialog.MessageForTest == "Scan cancelled." || !service.IsIndexing, "the scan to stop");
        Assert.AreEqual(1, service.RefreshCalls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ABackgroundScanIsNotAdvertisedAsStoppable()
    {
        // Ctrl+X only cancels a scan this dialog started. Over the periodic indexer's scan the footer
        // used to offer it anyway, and the key did nothing.
        var service = new FakeDocumentLibraryService();
        service.Documents.Add(Doc("a.txt"));
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 1);

        service.IsIndexing = true;
        service.Progress = new DocumentIndexingProgress(DocumentIndexingPhase.Indexing, 10, 3, "x.pdf");

        var screen = Render(dialog);
        AssertScreenDoesNotContain(screen, "Ctrl+X:stop scan");
        AssertScreenContains(screen, "scanning…");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.X, control: true));

        StringAssert.Contains(dialog.MessageForTest, "cannot be stopped from here");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlRDoesNotStartASecondScanWhileOneIsRunning()
    {
        var service = new FakeDocumentLibraryService { IsIndexing = true };
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.R, control: true));

        Assert.AreEqual(0, service.RefreshCalls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterRevealsTheDocumentsFolderInTheFocusedPanelAndCloses()
    {
        var service = new FakeDocumentLibraryService();
        var doc = Doc("a.txt");
        service.Documents.Add(doc);
        var contentModelService = new Mock<IContentModelService>();
        Url? navigatedTo = null;
        contentModelService.Setup(s => s.SetUrlAsync(It.IsAny<Url>()))
            .Callback<Url>(url => navigatedTo = url)
            .Returns(Task.CompletedTask);
        var dialog = CreateDialog(service, contentModelService.Object);
        var root = await ShowAsync(dialog);
        await SettleAsync(dialog, 1);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.IsNotNull(navigatedTo);
        StringAssert.StartsWith(navigatedTo!.Path.TrimEnd('/', '\\'), Path.GetDirectoryName(doc.Path)!.TrimEnd('/', '\\'));
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClosingUnsubscribesFromTheLibrary()
    {
        var service = new FakeDocumentLibraryService();
        service.Documents.Add(Doc("a.txt"));
        var dialog = CreateDialog(service);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 1);
        dialog.Close();
        var queriesBefore = service.Queries.Count;

        service.RaiseLibraryChanged();
        await Task.Delay(400);

        Assert.AreEqual(queriesBefore, service.Queries.Count);
        Assert.AreEqual(1, dialog.ResultCountForTest, "a closed dialog does not rebuild its listing");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WrapLinesBreaksAtWordsAndKeepsBlankLines()
    {
        var lines = DocumentManagerDialog.WrapLines("one two three\n\nfour", 8);

        CollectionAssert.AreEqual(new[] { "one two", "three", "", "four" }, lines);
    }
}

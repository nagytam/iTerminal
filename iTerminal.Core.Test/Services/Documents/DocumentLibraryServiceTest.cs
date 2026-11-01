using System.Text;
using iTerminal.Core.Data;
using iTerminal.Core.Data.Documents;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Documents;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Test.Services.Documents;

[TestClass]
public class DocumentLibraryServiceTest
{
    private string _tempRoot = string.Empty;
    private string _documentsFolder = string.Empty;
    private string _databasePath = string.Empty;
    private SqliteDbContextFactory _dbContextFactory = null!;
    private SettingsService _settingsService = null!;

    private sealed class SqliteDbContextFactory : IDbContextFactory<iTerminalDbContext>
    {
        private readonly DbContextOptions<iTerminalDbContext> _options;

        public SqliteDbContextFactory(string databasePath)
        {
            DbContextOptionsBuilder<iTerminalDbContext> builder = new();
            builder.UseSqlite($"Data Source={databasePath}");
            _options = builder.Options;
        }

        public iTerminalDbContext CreateDbContext() => new(_options);

        public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DocLibraryTest_" + Guid.NewGuid().ToString("N")[..8]);
        _documentsFolder = Path.Combine(_tempRoot, "Documents");
        Directory.CreateDirectory(_documentsFolder);

        _databasePath = Path.Combine(_tempRoot, "documents.sqlite");
        _dbContextFactory = new SqliteDbContextFactory(_databasePath);

        _settingsService = new SettingsService();
        _settingsService.Set(Settings.DocumentsFolder, _documentsFolder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
        }
    }

    private DocumentLibraryService CreateService() => new(_dbContextFactory, _settingsService);

    private string WriteDocument(string relativePath, string content)
    {
        string path = Path.Combine(_documentsFolder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DocumentsFolder_FallsBackToTheBuiltInDefaultWhenNothingIsConfigured()
    {
        SettingsService empty = new();

        string expected = OperatingSystem.IsLinux()
            ? "/home/tamas/nata/documents"
            : Path.Combine(
                Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? @"C:\",
                @"users\tamas\nata\documents");

        Assert.AreEqual(expected, DocumentLibraryService.ResolveDocumentsFolder(empty));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RefreshAsync_IndexesFilesAndExtractsTheirText()
    {
        WriteDocument("notes.txt", "The quarterly budget was approved on Tuesday.");
        WriteDocument(Path.Combine("plans", "roadmap.md"), "# Roadmap\n\nShip the parser in March.");

        DocumentLibraryService service = CreateService();
        int count = await service.RefreshAsync();

        Assert.AreEqual(2, count);
        Assert.AreEqual(2, service.DocumentCount);
        Assert.AreEqual(2, service.IndexedTextCount);

        DocumentEntry roadmap = service.Entries.Single(entry => entry.FileName == "roadmap.md");
        Assert.AreEqual("plans", roadmap.RelativeFolder);
        Assert.AreEqual(DocumentKind.Text, roadmap.Kind);
        Assert.AreEqual(DocumentExtractionStatus.Extracted, roadmap.ExtractionStatus);
        Assert.IsTrue(roadmap.WordCount > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SearchAsync_FindsADocumentByItsContent()
    {
        WriteDocument("budget.txt", "The quarterly budget was approved on Tuesday.");
        WriteDocument("recipe.txt", "Combine flour, sugar and butter.");

        DocumentLibraryService service = CreateService();
        await service.RefreshAsync();

        IReadOnlyList<DocumentSearchHit> hits = await service.SearchAsync("approved");

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("budget.txt", hits[0].Entry.FileName);
        Assert.AreEqual(DocumentSearchMatch.Content, hits[0].Match);
        StringAssert.Contains(hits[0].Snippet, "approved");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SearchAsync_RequiresEveryWordToMatch()
    {
        WriteDocument("one.txt", "alpha beta");
        WriteDocument("two.txt", "alpha gamma");

        DocumentLibraryService service = CreateService();
        await service.RefreshAsync();

        IReadOnlyList<DocumentSearchHit> hits = await service.SearchAsync("alpha beta");

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("one.txt", hits[0].Entry.FileName);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SearchAsync_MatchesAPartialWordSoResultsNarrowAsTheQueryIsTyped()
    {
        WriteDocument("invoice.txt", "This is an invoice for consulting.");

        DocumentLibraryService service = CreateService();
        await service.RefreshAsync();

        Assert.AreEqual(1, (await service.SearchAsync("invo")).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SearchAsync_PutsANameMatchAheadOfDocumentsThatOnlyMentionIt()
    {
        WriteDocument("budget.txt", "Nothing much in here.");
        WriteDocument("minutes.txt", "We discussed the budget at length, and the budget again.");

        DocumentLibraryService service = CreateService();
        await service.RefreshAsync();

        IReadOnlyList<DocumentSearchHit> hits = await service.SearchAsync("budget");

        Assert.AreEqual(2, hits.Count);
        Assert.AreEqual("budget.txt", hits[0].Entry.FileName);
        Assert.AreEqual(DocumentSearchMatch.Name, hits[0].Match);
        Assert.AreEqual(DocumentSearchMatch.Content, hits[1].Match);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SearchAsync_TreatsQuerySyntaxAsTextRatherThanAsOperators()
    {
        // "AND", "*" and a stray quote are all FTS5 syntax. A user typing them into a search box
        // means them literally, and an unescaped query would throw instead of searching.
        WriteDocument("notes.txt", "Interest rates and inflation.");

        DocumentLibraryService service = CreateService();
        await service.RefreshAsync();

        IReadOnlyList<DocumentSearchHit> hits = await service.SearchAsync("rates AND \"inflation*");

        Assert.AreEqual(1, hits.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SearchAsync_ReturnsNothingForAnEmptyQuery()
    {
        WriteDocument("notes.txt", "anything");

        DocumentLibraryService service = CreateService();
        await service.RefreshAsync();

        Assert.AreEqual(0, (await service.SearchAsync("   ")).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GetTextAsync_ReturnsTheCachedTextForThePreview()
    {
        string path = WriteDocument("notes.txt", "Preview me.");

        DocumentLibraryService service = CreateService();
        await service.RefreshAsync();

        Assert.AreEqual("Preview me.", await service.GetTextAsync(path));
        Assert.AreEqual(string.Empty, await service.GetTextAsync(Path.Combine(_documentsFolder, "missing.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RefreshAsync_LeavesAnUnchangedFileAlone()
    {
        string path = WriteDocument("notes.txt", "Unchanged.");

        DocumentLibraryService service = CreateService();
        await service.RefreshAsync();
        DateTimeOffset firstIndexedUtc = service.Entries.Single().IndexedUtc;

        await service.RefreshAsync();

        // Same stamp means the second pass reused the cached row rather than re-extracting — the
        // whole reason a rescan of a folder of PDFs is affordable.
        Assert.AreEqual(firstIndexedUtc, service.Entries.Single().IndexedUtc);
        Assert.AreEqual(path, service.Entries.Single().Path);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RefreshAsync_ReExtractsAFileThatChanged()
    {
        string path = WriteDocument("notes.txt", "Original wording.");

        DocumentLibraryService service = CreateService();
        await service.RefreshAsync();

        File.WriteAllText(path, "Replacement wording entirely.");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

        await service.RefreshAsync();

        Assert.AreEqual("Replacement wording entirely.", await service.GetTextAsync(path));
        Assert.AreEqual(1, (await service.SearchAsync("Replacement")).Count);
        Assert.AreEqual(0, (await service.SearchAsync("Original")).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RefreshAsync_DropsADocumentWhoseFileIsGone()
    {
        string path = WriteDocument("gone.txt", "Temporary content.");
        WriteDocument("stays.txt", "Permanent content.");

        DocumentLibraryService service = CreateService();
        await service.RefreshAsync();

        File.Delete(path);
        await service.RefreshAsync();

        Assert.AreEqual(1, service.DocumentCount);
        Assert.AreEqual("stays.txt", service.Entries.Single().FileName);

        // The content and the full-text index have to go with it, or a search would keep matching a
        // document the user deleted and offer a preview of a file that is not there.
        Assert.AreEqual(0, (await service.SearchAsync("Temporary")).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RefreshAsync_IndexesAnUnsupportedFileByNameAndSaysWhyItHasNoText()
    {
        WriteDocument("archive.zzz", "opaque");

        DocumentLibraryService service = CreateService();
        await service.RefreshAsync();

        DocumentEntry entry = service.Entries.Single();
        Assert.AreEqual(DocumentExtractionStatus.Unsupported, entry.ExtractionStatus);
        Assert.AreEqual(0, entry.TextLength);

        // Still findable by name, which is the point of listing it at all.
        Assert.AreEqual(1, (await service.SearchAsync("archive")).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RefreshAsync_SkipsBuildOutputAndDotFiles()
    {
        WriteDocument("real.txt", "keep me");
        WriteDocument(Path.Combine("obj", "generated.txt"), "build output");
        WriteDocument(Path.Combine(".git", "COMMIT_EDITMSG"), "internals");
        WriteDocument(".hidden.txt", "dot file");
        WriteDocument("~$open.docx", "office lock file");

        DocumentLibraryService service = CreateService();
        await service.RefreshAsync();

        Assert.AreEqual(1, service.DocumentCount);
        Assert.AreEqual("real.txt", service.Entries.Single().FileName);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RefreshAsync_ReportsBothStagesAndEndsIdle()
    {
        for (int index = 0; index < 5; index++)
        {
            WriteDocument($"file{index}.txt", $"content {index}");
        }

        DocumentLibraryService service = CreateService();

        List<DocumentIndexingProgress> reports = [];
        service.IndexingProgressChanged += (_, progress) =>
        {
            lock (reports)
            {
                reports.Add(progress);
            }
        };

        await service.RefreshAsync();

        Assert.IsTrue(reports.Any(report => report.Phase == DocumentIndexingPhase.Discovering));
        Assert.IsTrue(reports.Any(report => report.Phase == DocumentIndexingPhase.Indexing));
        Assert.AreEqual(DocumentIndexingPhase.Idle, reports[^1].Phase);
        Assert.IsFalse(service.IsIndexing);

        DocumentIndexingProgress indexing = reports.Last(report => report.Phase == DocumentIndexingPhase.Indexing);
        Assert.IsTrue(indexing.IsDiscoveryComplete);
        Assert.AreEqual(5, indexing.DiscoveredFiles);
        Assert.AreEqual(100, indexing.Percent);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RefreshAsync_ReportsAMissingFolderRatherThanThrowing()
    {
        _settingsService.Set(Settings.DocumentsFolder, Path.Combine(_tempRoot, "nowhere"));

        DocumentLibraryService service = CreateService();
        int count = await service.RefreshAsync();

        Assert.AreEqual(0, count);
        StringAssert.Contains(service.StatusMessage, "not found");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Entries_ComeBackFromTheDatabaseInASecondSessionWithoutRescanning()
    {
        WriteDocument("notes.txt", "Persisted across sessions.");

        DocumentLibraryService first = CreateService();
        await first.RefreshAsync();

        // A brand-new service over the same database — the state at the start of the next run,
        // before any scan has had a chance to happen.
        DocumentLibraryService second = CreateService();

        Assert.AreEqual(1, second.DocumentCount);
        Assert.AreEqual(1, (await second.SearchAsync("Persisted")).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SearchIndex_IsRebuiltFromTheContentTableWhenItGoesMissing()
    {
        // The FTS table is invisible to EF, so anything that recreates the schema can leave the
        // content table behind an index that no longer exists. Recovering has to be free; the
        // alternative is re-extracting every document to rebuild something derived from what is
        // already stored.
        WriteDocument("notes.txt", "Rebuildable content.");

        DocumentLibraryService service = CreateService();
        await service.RefreshAsync();

        await using (iTerminalDbContext context = _dbContextFactory.CreateDbContext())
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE DocumentSearch;");
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        DocumentLibraryService recovered = CreateService();
        Assert.AreEqual(1, (await recovered.SearchAsync("Rebuildable")).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Tokenize_SplitsOnPunctuationAndDropsIt()
    {
        CollectionAssert.AreEqual(
            new[] { "quarterly", "budget", "2026" },
            DocumentSearchIndex.Tokenize("  quarterly, budget — 2026! "));

        Assert.AreEqual(0, DocumentSearchIndex.Tokenize("   ").Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BuildMatchExpression_QuotesEveryTokenAndRequiresThemAll()
    {
        Assert.AreEqual("\"alpha\"* AND \"beta\"*", DocumentSearchIndex.BuildMatchExpression(["alpha", "beta"]));

        // A quote inside a token has to be doubled, or it closes the one the builder opened.
        Assert.AreEqual("\"a\"\"b\"*", DocumentSearchIndex.BuildMatchExpression(["a\"b"]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetRelativeFolder_IsEmptyForAFileInTheRoot()
    {
        Assert.AreEqual(string.Empty, DocumentLibraryService.GetRelativeFolder(_documentsFolder,
            Path.Combine(_documentsFolder, "notes.txt")));

        Assert.AreEqual("plans", DocumentLibraryService.GetRelativeFolder(_documentsFolder,
            Path.Combine(_documentsFolder, "plans", "roadmap.md")));
    }
}

using System.Text;
using iTerminal.Core.Data;
using iTerminal.Core.Data.Documents;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Documents;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Test.Services.Documents;

/// <summary>
/// The meaning index end to end over the real library service, with a fake embedder: two topics, and a passage is
/// placed by how many of each topic's words it contains. "money" lands with the finance words although the word itself
/// is in no file, which is the whole point of the feature.
/// </summary>
[TestClass]
public class DocumentMeaningSearchTest
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

    /// <summary>Two topics, one dimension each. A text's vector counts its words per topic, with a small floor so none is zero.</summary>
    private sealed class TopicEmbedder(string modelId) : IDocumentEmbedder
    {
        private static readonly string[][] Topics =
        [
            ["tax", "invoice", "money", "payment", "receipt", "filing"],
            ["pasta", "recipe", "oven", "tomato", "bake"],
        ];

        private readonly List<string> _embedded = [];

        public string ModelId { get; } = modelId;

        public bool IsConfigured => true;

        /// <summary>When set, every request fails with it, as an embedding model that is not pulled does.</summary>
        public Exception? Failure { get; set; }

        /// <summary>When set, a request containing this word is refused, as a model refuses one passage it cannot embed.</summary>
        public string? RefuseWord { get; set; }

        public List<string> Embedded
        {
            get
            {
                lock (_embedded)
                {
                    return [.. _embedded];
                }
            }
        }

        public void ClearEmbedded()
        {
            lock (_embedded)
            {
                _embedded.Clear();
            }
        }

        /// <summary>Runs once a request has arrived, before it is answered: where a test changes the library mid-embedding.</summary>
        public Action? BeforeEmbed { get; set; }

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            BeforeEmbed?.Invoke();

            lock (_embedded)
            {
                _embedded.AddRange(texts);
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            if (RefuseWord is not null && texts.Any(text => text.Contains(RefuseWord, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("the model refuses this passage");
            }

            IReadOnlyList<float[]> vectors = [.. texts.Select(Vectorize)];
            return Task.FromResult(vectors);
        }

        private static float[] Vectorize(string text)
        {
            string lower = text.ToLowerInvariant();
            float[] vector = new float[Topics.Length];
            for (int topic = 0; topic < Topics.Length; topic++)
            {
                vector[topic] = 0.01f + Topics[topic].Count(word => lower.Contains(word, StringComparison.Ordinal));
            }

            return vector;
        }
    }

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MeaningSearchTest_" + Guid.NewGuid().ToString("N")[..8]);
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

    private DocumentLibraryService CreateService(IDocumentEmbedder? embedder) =>
        new(_dbContextFactory, _settingsService, documentEmbedder: embedder);

    private string WriteDocument(string relativePath, string content)
    {
        string path = Path.Combine(_documentsFolder, relativePath);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    private const string TaxText = "Declare your income and submit the annual filing. The payment is due in April.";
    private const string DinnerText = "Pasta with tomato sauce, baked in the oven.";

    [Timeout(60_000)]
    [TestMethod]
    public async Task MeaningFindsADocumentThatSharesNoWordWithTheQuery()
    {
        DocumentLibraryService service = CreateService(new TopicEmbedder("m"));
        WriteDocument("tax-return.txt", TaxText);
        WriteDocument("dinner.txt", DinnerText);
        await service.RefreshAsync();

        IReadOnlyList<DocumentSearchHit> hits = await service.SearchSemanticAsync("money");

        Assert.AreEqual("tax-return.txt", hits[0].Entry.FileName);
        Assert.AreEqual(DocumentSearchMatch.Semantic, hits[0].Match);
        StringAssert.StartsWith(hits[0].Snippet, "Declare your income", "the snippet is the passage that scored");
        Assert.AreEqual(0, (await service.SearchAsync("money")).Count, "the words search still finds nothing for it");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnlyAChangedDocumentIsEmbeddedAgain()
    {
        var embedder = new TopicEmbedder("m");
        DocumentLibraryService service = CreateService(embedder);
        WriteDocument("tax-return.txt", TaxText);
        WriteDocument("dinner.txt", DinnerText);
        await service.RefreshAsync();
        embedder.ClearEmbedded();

        const string changed = "Declare your income and submit the annual filing. Bake the receipts in the oven.";
        WriteDocument("tax-return.txt", changed);
        await service.RefreshAsync();

        CollectionAssert.AreEqual(new[] { changed }, embedder.Embedded);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ChangingTheModelEmbedsTheWholeLibraryAgain()
    {
        WriteDocument("tax-return.txt", TaxText);
        WriteDocument("dinner.txt", DinnerText);
        await CreateService(new TopicEmbedder("first")).RefreshAsync();

        var embedder = new TopicEmbedder("second");
        DocumentLibraryService service = CreateService(embedder);
        await service.RefreshAsync();

        CollectionAssert.AreEqual(new[] { TaxText, DinnerText }.OrderBy(text => text, StringComparer.Ordinal).ToArray(), embedder.Embedded.OrderBy(text => text, StringComparer.Ordinal).ToArray());
        StringAssert.Contains(service.SemanticMessage, "embedded with second");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARemovedDocumentLeavesTheMeaningIndex()
    {
        DocumentLibraryService service = CreateService(new TopicEmbedder("m"));
        string tax = WriteDocument("tax-return.txt", TaxText);
        WriteDocument("dinner.txt", DinnerText);
        await service.RefreshAsync();

        File.Delete(tax);
        await service.RefreshAsync();

        IReadOnlyList<DocumentSearchHit> hits = await service.SearchSemanticAsync("money");
        CollectionAssert.DoesNotContain(hits.Select(hit => hit.Entry.FileName).ToList(), "tax-return.txt");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADocumentRemovedWhileItIsBeingEmbeddedLeavesNoRecordBehind()
    {
        var embedder = new TopicEmbedder("m");
        DocumentLibraryService service = CreateService(embedder);

        // "dinner.txt" is embedded first (the pending list is sorted); while it is in flight, the other file goes and a scan notices.
        WriteDocument("dinner.txt", DinnerText);
        string tax = WriteDocument("tax-return.txt", TaxText);
        embedder.BeforeEmbed = () =>
        {
            embedder.BeforeEmbed = null;
            File.Delete(tax);
            service.RefreshAsync().GetAwaiter().GetResult();
        };
        await service.RefreshAsync();

        CollectionAssert.DoesNotContain(
            (await service.SearchSemanticAsync("money")).Select(hit => hit.Entry.FileName).ToList(),
            "tax-return.txt",
            "a removed document is not recorded as embedded");

        // Recreated at the same path, it is new to the index and is embedded like any other new file.
        WriteDocument("tax-return.txt", TaxText);
        embedder.ClearEmbedded();
        await service.RefreshAsync();

        CollectionAssert.Contains(embedder.Embedded, TaxText);
        Assert.AreEqual("tax-return.txt", (await service.SearchSemanticAsync("money"))[0].Entry.FileName);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADocumentTheModelRefusesDoesNotStopTheOthers()
    {
        // "a-refused" sorts first, so a run that stopped at the first refusal would never reach "tax-return".
        var embedder = new TopicEmbedder("m") { RefuseWord = "BAD" };
        DocumentLibraryService service = CreateService(embedder);
        WriteDocument("a-refused.txt", "BAD passage about nothing in particular");
        WriteDocument("tax-return.txt", TaxText);

        await service.RefreshAsync();

        StringAssert.Contains(service.SemanticMessage, "1 document(s) could not be embedded");
        Assert.AreEqual("tax-return.txt", (await service.SearchSemanticAsync("money"))[0].Entry.FileName);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutAnEmbeddingModelMeaningSearchIsOffAndTheScanStillRuns()
    {
        DocumentLibraryService service = CreateService(embedder: null);
        WriteDocument("tax-return.txt", TaxText);

        int count = await service.RefreshAsync();

        Assert.AreEqual(1, count);
        Assert.IsFalse(service.SemanticSearchAvailable);
        StringAssert.Contains(service.SemanticMessage, "off");
        Assert.AreEqual(0, (await service.SearchSemanticAsync("money")).Count);
        Assert.AreEqual(1, (await service.SearchAsync("payment")).Count, "keyword search is unaffected");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailingModelDoesNotFailTheScanOrKeywordSearch()
    {
        var embedder = new TopicEmbedder("m") { Failure = new InvalidOperationException("model not pulled") };
        DocumentLibraryService service = CreateService(embedder);
        WriteDocument("tax-return.txt", TaxText);

        int count = await service.RefreshAsync();

        Assert.AreEqual(1, count);
        StringAssert.Contains(service.SemanticMessage, "model not pulled");
        Assert.AreEqual(1, (await service.SearchAsync("payment")).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AReadyIndexSaysHowManyDocumentsItCovers()
    {
        DocumentLibraryService service = CreateService(new TopicEmbedder("m"));
        WriteDocument("tax-return.txt", TaxText);
        WriteDocument("dinner.txt", DinnerText);

        await service.RefreshAsync();

        StringAssert.Contains(service.SemanticMessage, "Meaning index: 2 documents, embedded with m.");
    }
}

using iTerminal.Core.Data;
using iTerminal.Core.Data.Documents;
using iTerminal.Core.Services.Documents;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Test.Services.Documents;

[TestClass]
public class DocumentVectorIndexTest
{
    private string _tempRoot = string.Empty;
    private DbContextOptions<iTerminalDbContext> _options = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "VectorIndexTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
        _options = new DbContextOptionsBuilder<iTerminalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_tempRoot, "vectors.sqlite")}")
            .Options;

        using iTerminalDbContext context = new(_options);
        context.Database.EnsureCreated();
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

    private iTerminalDbContext Context() => new(_options);

    private void AddText(string path, string text)
    {
        using iTerminalDbContext context = Context();

        // A listed document has both rows: the metadata the library lists, and the text it indexed.
        context.DocumentEntries.Add(new DocumentEntry { Path = path, FileName = Path.GetFileName(path), Kind = DocumentKind.Text });
        context.DocumentContentEntries.Add(new DocumentContentEntry { Path = path, Text = text });
        context.SaveChanges();
    }

    [TestMethod]
    public void VectorsRoundTripThroughTheirStoredBytes()
    {
        float[] vector = [0.25f, -1.5f, 3f, float.Epsilon];

        CollectionAssert.AreEqual(vector, DocumentVectorIndex.FromBytes(DocumentVectorIndex.ToBytes(vector)));
    }

    [TestMethod]
    public void SearchRanksEachDocumentByItsBestPassageMostSimilarFirst()
    {
        var index = new DocumentVectorIndex();
        using iTerminalDbContext context = Context();
        index.Replace(context, "/docs/a.txt", "m", [(0, "passage", [0f, 1f]), (800, "passage", [1f, 0f])]);
        index.Replace(context, "/docs/b.txt", "m", [(0, "passage", [0.8f, 0.2f])]);

        IReadOnlyList<DocumentVectorIndex.Match> matches = index.Search(context, "m", [1f, 0f], limit: 10);

        Assert.AreEqual(2, matches.Count);
        Assert.AreEqual("/docs/a.txt", matches[0].Path);
        Assert.AreEqual(800, matches[0].ChunkStart, "the passage that matched, not the first one");
        Assert.AreEqual(1.0, matches[0].Score, 1e-6);
        Assert.AreEqual("/docs/b.txt", matches[1].Path);
        Assert.IsTrue(matches[1].Score < matches[0].Score);
    }

    [TestMethod]
    public void SearchReturnsNothingForAnotherModelAndHonoursTheLimit()
    {
        var index = new DocumentVectorIndex();
        using iTerminalDbContext context = Context();
        index.Replace(context, "/docs/a.txt", "m", [(0, "passage", [1f, 0f])]);
        index.Replace(context, "/docs/b.txt", "m", [(0, "passage", [0.9f, 0.1f])]);

        Assert.AreEqual(0, index.Search(context, "other-model", [1f, 0f], limit: 10).Count);
        Assert.AreEqual(1, index.Search(context, "m", [1f, 0f], limit: 1).Count);
    }

    [TestMethod]
    public void PassagesOfADifferentLengthAreSkippedRatherThanCompared()
    {
        var index = new DocumentVectorIndex();
        using iTerminalDbContext context = Context();
        index.Replace(context, "/docs/a.txt", "m", [(0, "passage", [1f, 0f, 0f])]);

        Assert.AreEqual(0, index.Search(context, "m", [1f, 0f], limit: 10).Count);
    }

    [TestMethod]
    public void PendingListsDocumentsWithoutVectorsOrWithAnotherModelsVectors()
    {
        AddText("/docs/a.txt", "alpha");
        AddText("/docs/b.txt", "beta");
        AddText("/docs/c.txt", "gamma");
        var index = new DocumentVectorIndex();
        using iTerminalDbContext context = Context();
        index.Replace(context, "/docs/a.txt", "m", [(0, "passage", [1f, 0f])]);

        CollectionAssert.AreEqual(new[] { "/docs/b.txt", "/docs/c.txt" }, index.Pending(context, "m"));
        CollectionAssert.AreEqual(new[] { "/docs/a.txt", "/docs/b.txt", "/docs/c.txt" }, index.Pending(context, "other-model"));
    }

    [TestMethod]
    public void ADocumentWithNoPassagesIsStillRecordedAsEmbedded()
    {
        AddText("/docs/empty.txt", string.Empty);
        var index = new DocumentVectorIndex();
        using iTerminalDbContext context = Context();

        index.Replace(context, "/docs/empty.txt", "m", []);

        CollectionAssert.DoesNotContain(index.Pending(context, "m"), "/docs/empty.txt");
        Assert.AreEqual(1, index.Count(context, "m"));
    }

    [TestMethod]
    public void RemoveForgetsTheVectorsAndTheModelRecord()
    {
        var index = new DocumentVectorIndex();
        using iTerminalDbContext context = Context();
        index.Replace(context, "/docs/a.txt", "m", [(0, "passage", [1f, 0f])]);
        index.Replace(context, "/docs/b.txt", "m", [(0, "passage", [0.5f, 0.5f])]);

        index.Remove(context, ["/docs/a.txt"]);

        Assert.AreEqual(1, index.Count(context, "m"));
        CollectionAssert.AreEqual(new[] { "/docs/b.txt" }, index.Search(context, "m", [1f, 0f], limit: 10).Select(match => match.Path).ToArray());
    }

    [TestMethod]
    public void ASearchAfterAWriteSeesTheWrite()
    {
        var index = new DocumentVectorIndex();
        using iTerminalDbContext context = Context();
        index.Replace(context, "/docs/a.txt", "m", [(0, "passage", [0f, 1f])]);
        Assert.AreEqual(0, index.Search(context, "m", [1f, 0f], limit: 10)[0].Score, 1e-6, "a warm snapshot is in use");

        index.Replace(context, "/docs/a.txt", "m", [(0, "passage", [1f, 0f])]);

        Assert.AreEqual(1.0, index.Search(context, "m", [1f, 0f], limit: 10)[0].Score, 1e-6);
    }

    [TestMethod]
    public void APassageIsReadBackByItsOffsetWithoutTheDocumentText()
    {
        // Non-BMP characters before the second passage: an offset into the document text would not land on it.
        var index = new DocumentVectorIndex();
        using iTerminalDbContext context = Context();
        index.Replace(context, "/docs/a.txt", "m", [(0, "first", [1f, 0f]), (800, "💡second", [0f, 1f])]);

        Assert.AreEqual("💡second", index.ReadPassage(context, "/docs/a.txt", 800));
        Assert.AreEqual(string.Empty, index.ReadPassage(context, "/docs/a.txt", 1600));
    }
}

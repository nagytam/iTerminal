using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Evidence;
using iTerminal.Core.Services.Timestamps;
using iTerminal.Core.Test.Services.Timestamps;
using Moq;

namespace iTerminal.Core.Test.Services.Evidence;

/// <summary>Every entry is time-stamped over its digest as it is added, and Verify checks the stamp.</summary>
[TestClass]
public class EvidenceTimestampTest
{
    private string _root = null!;
    private string _notebookFolder = null!;

    [TestInitialize]
    public void CreateSandbox()
    {
        _root = Directory.CreateTempSubdirectory("iTerminal.EvidenceStamp.").FullName;
        _notebookFolder = Path.Combine(_root, "case-42");
    }

    [TestCleanup]
    public void RemoveSandbox()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static byte[] Sha256Of(string text) => SHA256.HashData(Encoding.UTF8.GetBytes(text));

    [TestMethod]
    public async Task AnEntryAddedWithAnAuthorityCarriesAStampThatVerifies()
    {
        var notebook = new EvidenceNotebookService(new FakeTsa());
        await notebook.OpenAsync(_notebookFolder);

        var record = await notebook.AddTextAsync(EvidenceKind.Note, "Door found open");

        Assert.IsNotNull(record.Timestamp);
        Assert.AreEqual(FakeTsa.Address, record.Timestamp.Authority);
        Assert.AreEqual(FakeTsa.GeneratedAt.UtcDateTime, record.Timestamp.GeneratedUtc);
        Assert.AreEqual(0, (await notebook.VerifyAsync()).Count);
    }

    [TestMethod]
    public async Task AFileIsStampedOverTheDigestOfTheBytesStored()
    {
        var content = Encoding.UTF8.GetBytes("the report");
        var source = new Mock<IFileSystem>();
        source.Setup(fs => fs.StreamToAsync(It.IsAny<Url>(), It.IsAny<Stream>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>()))
            .Returns((Url _, Stream destination, IProgress<long>? _, CancellationToken token) => destination.WriteAsync(content, token).AsTask());
        var tsa = new FakeTsa();
        var notebook = new EvidenceNotebookService(tsa);
        await notebook.OpenAsync(_notebookFolder);

        var record = await notebook.AddFileAsync(source.Object, new Url(Path.Combine(_root, "report.txt")), content.Length, null);

        Assert.AreEqual(1, tsa.Calls);
        Assert.IsNotNull(record.Timestamp);
        Assert.IsTrue(Rfc3161.IsValidFor(record.Timestamp, Sha256Of("the report")));
        Assert.AreEqual(0, (await notebook.VerifyAsync()).Count);
    }

    [TestMethod]
    public async Task AnEntryAddedWithNoAuthorityIsKeptAndVerifyReportsIt()
    {
        var notebook = new EvidenceNotebookService();
        await notebook.OpenAsync(_notebookFolder);

        var record = await notebook.AddTextAsync(EvidenceKind.Note, "Door found open");

        Assert.IsNull(record.Timestamp);
        Assert.AreEqual(1, (await notebook.ListAsync()).Count);
        var problems = await notebook.VerifyAsync();
        var problem = problems.Single();
        Assert.AreEqual("0001_note.txt", problem.FileName);
        StringAssert.Contains(problem.Message, "time stamp");
    }

    [TestMethod]
    public async Task AnAuthorityThatFailsKeepsTheEntryWithoutAStamp()
    {
        var authority = new Mock<ITimestampAuthority>();
        authority.Setup(service => service.TimestampAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimestampException("The time-stamp authority tsa.example did not answer."));
        var notebook = new EvidenceNotebookService(authority.Object);
        await notebook.OpenAsync(_notebookFolder);

        var record = await notebook.AddTextAsync(EvidenceKind.Clipboard, "text from the clipboard");

        Assert.IsNull(record.Timestamp);
        Assert.IsTrue(File.Exists(Path.Combine(_notebookFolder, EvidenceNotebookService.ItemsFolderName, record.FileName)));
        Assert.AreEqual(1, (await notebook.ListAsync()).Count);
    }

    [TestMethod]
    public async Task AStampOverAnotherDigestIsReportedByVerify()
    {
        var tsa = new FakeTsa { ImprintFor = _ => Sha256Of("some other content") };
        var notebook = new EvidenceNotebookService(tsa);
        await notebook.OpenAsync(_notebookFolder);

        var record = await notebook.AddTextAsync(EvidenceKind.Note, "Door found open");

        Assert.IsNotNull(record.Timestamp, "The authority answered, so the stamp is kept as it came.");
        var problem = (await notebook.VerifyAsync()).Single();
        Assert.AreEqual(record.FileName, problem.FileName);
        StringAssert.Contains(problem.Message, "does not match");
    }

    [TestMethod]
    public async Task TheStampIsKeptInTheIndexAndStillVerifiesAfterReopening()
    {
        var notebook = new EvidenceNotebookService(new FakeTsa());
        await notebook.OpenAsync(_notebookFolder);
        var added = await notebook.AddTextAsync(EvidenceKind.Link, "https://example.com/a");

        var reopened = new EvidenceNotebookService();
        await reopened.OpenAsync(_notebookFolder);

        var listed = (await reopened.ListAsync()).Single();
        Assert.AreEqual(added.Timestamp, listed.Timestamp);
        Assert.AreEqual(0, (await reopened.VerifyAsync()).Count);
    }

    [TestMethod]
    public async Task AnIndexWrittenBeforeStampsStillOpens()
    {
        var notebook = new EvidenceNotebookService();
        await notebook.OpenAsync(_notebookFolder);
        await notebook.AddTextAsync(EvidenceKind.Note, "older entry");

        // An index from before stamps existed has no Timestamp property at all.
        var indexPath = Path.Combine(_notebookFolder, EvidenceNotebookService.IndexFileName);
        var index = JsonNode.Parse(await File.ReadAllTextAsync(indexPath))!;
        foreach (var entry in index["Entries"]!.AsArray())
        {
            entry!.AsObject().Remove("Timestamp");
        }

        await File.WriteAllTextAsync(indexPath, index.ToJsonString());

        var reopened = new EvidenceNotebookService();
        await reopened.OpenAsync(_notebookFolder);
        Assert.IsNull((await reopened.ListAsync()).Single().Timestamp);
    }

    [TestMethod]
    public void TheFooterNamesTheAuthorityAndTheTimeOfAStamp()
    {
        var timestamp = new Rfc3161Timestamp(FakeTsa.Address, FakeTsa.GeneratedAt.UtcDateTime, "AAAA");

        var text = ContentModelEvidenceNotebook.TimestampText(timestamp);

        StringAssert.Contains(text, "RFC 3161");
        StringAssert.Contains(text, "2026-11-20 10:30:15");
        StringAssert.Contains(text, "tsa.example");
    }

    [TestMethod]
    public void TheFooterSaysWhenAnEntryIsNotStamped()
    {
        StringAssert.Contains(ContentModelEvidenceNotebook.TimestampText(null), "not time-stamped");
    }
}

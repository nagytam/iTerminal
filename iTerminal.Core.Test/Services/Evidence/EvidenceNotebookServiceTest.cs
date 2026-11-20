using System.Security.Cryptography;
using System.Text;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services.Evidence;
using iTerminal.Core.Test.Services.Timestamps;
using iTerminal.Core.Test.Helper;
using Moq;

namespace iTerminal.Core.Test.Services.Evidence;

[TestClass]
public class EvidenceNotebookServiceTest
{
    private string _root = null!;
    private string _notebookFolder = null!;

    [TestInitialize]
    public void CreateSandbox()
    {
        _root = Directory.CreateTempSubdirectory("iTerminal.Evidence.").FullName;
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

    private static string Sha256Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [TestMethod]
    public async Task OpenCreatesTheFolderTheIndexAndTheItemsFolder()
    {
        IEvidenceNotebookService notebook = new EvidenceNotebookService();

        await notebook.OpenAsync(_notebookFolder);

        Assert.IsTrue(notebook.IsOpen);
        Assert.AreEqual(Path.GetFullPath(_notebookFolder), notebook.FolderPath);
        Assert.IsTrue(File.Exists(Path.Combine(_notebookFolder, EvidenceNotebookService.IndexFileName)));
        Assert.IsTrue(Directory.Exists(Path.Combine(_notebookFolder, EvidenceNotebookService.ItemsFolderName)));
        Assert.AreEqual(0, (await notebook.ListAsync()).Count);
    }

    [TestMethod]
    public async Task ANoteRecordsItsTitleDigestAndTheOriginOfTheEntry()
    {
        IEvidenceNotebookService notebook = new EvidenceNotebookService();
        await notebook.OpenAsync(_notebookFolder);
        var before = DateTime.UtcNow.AddSeconds(-5);

        var record = await notebook.AddTextAsync(EvidenceKind.Note, "Door found open\nsecond line");

        var bytes = Encoding.UTF8.GetBytes("Door found open\nsecond line");
        Assert.AreEqual(1, record.Sequence);
        Assert.AreEqual("0001_note.txt", record.FileName);
        Assert.AreEqual(EvidenceKind.Note, record.Kind);
        Assert.AreEqual("Door found open", record.Title);
        Assert.AreEqual(bytes.Length, record.Size);
        Assert.AreEqual(Sha256Of(bytes), record.Sha256);
        Assert.AreEqual("typed in the notebook", record.Origin.SourceUrl);
        Assert.AreEqual(Environment.MachineName, record.Origin.Machine);
        Assert.AreEqual(Environment.UserName, record.Origin.OperatingUser);
        Assert.IsTrue(record.Origin.CapturedUtc >= before, "The capture time is taken when the entry is added.");
        Assert.IsTrue(record.Origin.CapturedUtc <= DateTime.UtcNow.AddSeconds(5));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(_notebookFolder, EvidenceNotebookService.ItemsFolderName, "0001_note.txt")));
    }

    [TestMethod]
    public async Task AFileIsCopiedIntoTheNotebookAndItsOriginIsRecorded()
    {
        var content = Encoding.UTF8.GetBytes("payload that is kept as evidence");
        var sourcePath = Path.Combine(_root, "report.pdf");
        var source = new Mock<IFileSystem>();
        source.Setup(fs => fs.StreamToAsync(It.IsAny<Url>(), It.IsAny<Stream>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>()))
            .Returns((Url _, Stream destination, IProgress<long>? _, CancellationToken token) => destination.WriteAsync(content, token).AsTask());

        IEvidenceNotebookService notebook = new EvidenceNotebookService();
        await notebook.OpenAsync(_notebookFolder);
        var modified = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);

        var record = await notebook.AddFileAsync(source.Object, new Url(sourcePath), content.Length, modified);

        Assert.AreEqual("0001_report.pdf", record.FileName);
        Assert.AreEqual(EvidenceKind.File, record.Kind);
        Assert.AreEqual("report.pdf", record.Title);
        Assert.AreEqual(content.Length, record.Size);
        Assert.AreEqual(Sha256Of(content), record.Sha256, "The digest is of the bytes that were stored.");
        Assert.AreEqual(new Url(sourcePath).FullUrl, record.Origin.SourceUrl);
        Assert.AreEqual(content.Length, record.Origin.SourceSize);
        Assert.AreEqual(modified, record.Origin.SourceModifiedUtc);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Path.Combine(_notebookFolder, EvidenceNotebookService.ItemsFolderName, record.FileName)));
    }

    [TestMethod]
    public async Task AFailedCopyLeavesNoPartialFileAndNoRecord()
    {
        var source = new Mock<IFileSystem>();
        source.Setup(fs => fs.StreamToAsync(It.IsAny<Url>(), It.IsAny<Stream>(), It.IsAny<IProgress<long>?>(), It.IsAny<CancellationToken>()))
            .Returns((Url _, Stream destination, IProgress<long>? _, CancellationToken token) =>
            {
                destination.Write(new byte[10], 0, 10);
                throw new IOException("the link dropped");
            });

        IEvidenceNotebookService notebook = new EvidenceNotebookService();
        await notebook.OpenAsync(_notebookFolder);

        await AsyncAssert.ThrowsAsync<IOException>(() => notebook.AddFileAsync(source.Object, new Url(Path.Combine(_root, "half.bin")), null, null));

        Assert.AreEqual(0, (await notebook.ListAsync()).Count);
        Assert.AreEqual(0, Directory.GetFiles(Path.Combine(_notebookFolder, EvidenceNotebookService.ItemsFolderName)).Length);
    }

    [TestMethod]
    public async Task EntriesAreNumberedInTheOrderTheyWereAdded()
    {
        IEvidenceNotebookService notebook = new EvidenceNotebookService();
        await notebook.OpenAsync(_notebookFolder);

        var first = await notebook.AddTextAsync(EvidenceKind.Note, "one");
        var second = await notebook.AddTextAsync(EvidenceKind.Link, "https://example.com/a");
        var third = await notebook.AddTextAsync(EvidenceKind.Clipboard, "three");

        Assert.AreEqual(1, first.Sequence);
        Assert.AreEqual(2, second.Sequence);
        Assert.AreEqual("0002_link.txt", second.FileName);
        Assert.AreEqual(3, third.Sequence);
        Assert.AreEqual("https://example.com/a", second.Origin.SourceUrl, "A link's origin is the link itself.");
        CollectionAssert.AreEqual(new[] { first, second, third }, (await notebook.ListAsync()).ToList());
    }

    [TestMethod]
    public async Task TheNotebookReadsBackWhatAnEarlierSessionWrote()
    {
        var first = new EvidenceNotebookService();
        await first.OpenAsync(_notebookFolder);
        var written = await first.AddTextAsync(EvidenceKind.Note, "kept across runs");

        var later = new EvidenceNotebookService();
        await later.OpenAsync(_notebookFolder);
        var listed = await later.ListAsync();

        Assert.AreEqual(1, listed.Count);
        Assert.AreEqual(written.Sha256, listed[0].Sha256);
        Assert.AreEqual(written.Origin.Machine, listed[0].Origin.Machine);
        Assert.AreEqual(2, (await later.AddTextAsync(EvidenceKind.Note, "next")).Sequence, "Numbering continues after the entries already there.");
    }

    [TestMethod]
    public async Task OpeningAFolderWithAnUnknownFormatIsRefused()
    {
        Directory.CreateDirectory(_notebookFolder);
        File.WriteAllText(Path.Combine(_notebookFolder, EvidenceNotebookService.IndexFileName),
            """{"Format": 99, "Id": "x", "Creator": null, "Entries": []}""");

        IEvidenceNotebookService notebook = new EvidenceNotebookService();

        await AsyncAssert.ThrowsAsync<InvalidDataException>(() => notebook.OpenAsync(_notebookFolder));
        Assert.IsFalse(notebook.IsOpen);
    }

    [TestMethod]
    [DataRow("../escape.txt")]
    [DataRow("..")]
    [DataRow("sub/entry.txt")]
    public async Task AnIndexThatNamesAFileOutsideItemsIsRefused(string fileName)
    {
        Directory.CreateDirectory(_notebookFolder);
        var index = $$"""{"Format": 1, "Id": "x", "Creator": null, "Entries": [{"Sequence": 1, "FileName": "{{fileName.Replace("\\", "\\\\")}}", "Kind": "Note", "Title": "t", "Size": 0, "Sha256": "", "Origin": null, "DurationSeconds": null}]}""";
        File.WriteAllText(Path.Combine(_notebookFolder, EvidenceNotebookService.IndexFileName), index);
        IEvidenceNotebookService notebook = new EvidenceNotebookService();

        await AsyncAssert.ThrowsAsync<InvalidDataException>(() => notebook.OpenAsync(_notebookFolder));
    }

    [TestMethod]
    public async Task VerifyReportsAChangedAndAMissingEntry()
    {
        // Stamped, so that the baseline verifies clean and only the two changes are reported.
        IEvidenceNotebookService notebook = new EvidenceNotebookService(new FakeTsa());
        await notebook.OpenAsync(_notebookFolder);
        var note = await notebook.AddTextAsync(EvidenceKind.Note, "abc");
        var voice = await notebook.AddVoiceAsync(new byte[3200], 16000);
        Assert.AreEqual(0, (await notebook.VerifyAsync()).Count);

        var items = Path.Combine(_notebookFolder, EvidenceNotebookService.ItemsFolderName);
        File.WriteAllText(Path.Combine(items, note.FileName), "xyz");
        File.Delete(Path.Combine(items, voice.FileName));

        var problems = await notebook.VerifyAsync();

        Assert.AreEqual(2, problems.Count);
        Assert.AreEqual(note.FileName, problems[0].FileName);
        Assert.AreEqual(voice.FileName, problems[1].FileName);
        StringAssert.Contains(problems[1].Message, "missing");
    }

    [TestMethod]
    public async Task VerifyReportsAnEntryWhoseSizeChanged()
    {
        IEvidenceNotebookService notebook = new EvidenceNotebookService();
        await notebook.OpenAsync(_notebookFolder);
        var note = await notebook.AddTextAsync(EvidenceKind.Note, "abc");
        File.WriteAllText(Path.Combine(_notebookFolder, EvidenceNotebookService.ItemsFolderName, note.FileName), "abcdef");

        var problems = await notebook.VerifyAsync();

        Assert.AreEqual(1, problems.Count);
        StringAssert.Contains(problems[0].Message, "size");
    }

    [TestMethod]
    public async Task AVoiceRecordingIsKeptAsAWavFileWithItsDuration()
    {
        IEvidenceNotebookService notebook = new EvidenceNotebookService();
        await notebook.OpenAsync(_notebookFolder);
        var pcm = new byte[32000];

        var record = await notebook.AddVoiceAsync(pcm, 16000);

        Assert.AreEqual(EvidenceKind.Voice, record.Kind);
        Assert.AreEqual("0001_voice.wav", record.FileName);
        Assert.AreEqual(1.0, record.DurationSeconds);
        Assert.AreEqual(44 + pcm.Length, record.Size);
        var file = File.ReadAllBytes(Path.Combine(_notebookFolder, EvidenceNotebookService.ItemsFolderName, record.FileName));
        Assert.AreEqual("RIFF", Encoding.ASCII.GetString(file, 0, 4));
        Assert.AreEqual("WAVE", Encoding.ASCII.GetString(file, 8, 4));
        Assert.AreEqual(Sha256Of(file), record.Sha256);
    }

    [TestMethod]
    public async Task TheNotebookIsReadOnlyThroughTheFileSystemInterface()
    {
        IEvidenceNotebookService notebook = new EvidenceNotebookService();
        await notebook.OpenAsync(_notebookFolder);
        var note = await notebook.AddTextAsync(EvidenceKind.Note, "read me");
        var url = new Url(Container.EVIDENCE + note.FileName);

        Assert.IsTrue(notebook.IsReadOnly);
        Assert.IsTrue(notebook.ExistsFile(url));
        Assert.IsTrue(notebook.ExistsFolder(new Url(Container.EVIDENCE)));
        Assert.IsFalse(notebook.ExistsFile(new Url(Container.EVIDENCE + "0009_missing.txt")));

        using var copy = new MemoryStream();
        await notebook.StreamToAsync(url, copy, null, CancellationToken.None);
        Assert.AreEqual("read me", Encoding.UTF8.GetString(copy.ToArray()));

        await AsyncAssert.ThrowsAsync<NotSupportedException>(() => Task.Run(() => notebook.DeleteFile(url, moveToRecycleBin: true)));
        await AsyncAssert.ThrowsAsync<NotSupportedException>(() => notebook.WriteFileAsync(url, new MemoryStream()));
        Assert.AreEqual(1, (await notebook.ListAsync()).Count, "A refused delete leaves the entry in place.");
    }

    [TestMethod]
    public async Task ATextAddRefusesAKindThatIsNotText()
    {
        IEvidenceNotebookService notebook = new EvidenceNotebookService();
        await notebook.OpenAsync(_notebookFolder);

        await AsyncAssert.ThrowsAsync<ArgumentException>(() => notebook.AddTextAsync(EvidenceKind.Voice, "not text"));
    }

    [TestMethod]
    public async Task AddingBeforeOpeningIsRefused()
    {
        IEvidenceNotebookService notebook = new EvidenceNotebookService();

        await AsyncAssert.ThrowsAsync<InvalidOperationException>(() => notebook.AddTextAsync(EvidenceKind.Note, "nowhere to go"));
    }

    [TestMethod]
    [DataRow("../../escape.txt", "_.._escape.txt")]
    [DataRow("a/b\\c.txt", "a_b_c.txt")]
    [DataRow("...", "item")]
    [DataRow("  ", "item")]
    public void NamesAreCleanedToOneSafeFileName(string name, string expected) =>
        Assert.AreEqual(expected, EvidenceNotebookService.SafeFileName(name));

    [TestMethod]
    public void ALongNameKeepsItsExtension()
    {
        var name = new string('n', 300) + ".pdf";

        var safe = EvidenceNotebookService.SafeFileName(name);

        Assert.IsTrue(safe.Length <= 120);
        Assert.IsTrue(safe.EndsWith(".pdf", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TheEvidenceDriveUrlIsItsOwnContainerType()
    {
        var url = new Url(Container.EVIDENCE + "0001_note.txt");

        Assert.AreEqual(ContainerType.EvidenceNotebook, url.ContainerType);
        Assert.AreEqual("0001_note.txt", url.Path.Trim('/'));
    }
}

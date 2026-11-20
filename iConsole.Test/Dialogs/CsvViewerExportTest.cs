using System.Text;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Prompts;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Dialogs;

[TestClass]
public class CsvViewerExportTest
{
    // Built with the platform's separator: Url.ParentUrl splits on Path.DirectorySeparatorChar, so a "/" path breaks on Windows.
    private static readonly string Sales = Path.Combine(Path.GetTempPath(), "data", "sales.csv");
    private static readonly string Notes = Path.Combine(Path.GetTempPath(), "data", "notes.tsv");

    private sealed class Fixture
    {
        public required CsvViewerDialog Dialog { get; init; }
        public required Mock<IContentModel> FileSystem { get; init; }
        public required Mock<IPromptService> Prompts { get; init; }
    }

    private static Fixture CreateFixture(string path, string text)
    {
        var document = new Url(path);
        Mock<IMemoryFile> file = new();
        file.SetupGet(f => f.Content).Returns(Encoding.UTF8.GetBytes(text));
        Mock<IContentModel> fileSystem = new();
        fileSystem.Setup(f => f.ReadFileAsync(document)).ReturnsAsync(file.Object);
        fileSystem.Setup(f => f.WriteFileAsync(It.IsAny<Url>(), It.IsAny<MemoryStream>())).Returns(Task.CompletedTask);
        fileSystem.Setup(f => f.ExistsFolder(It.IsAny<Url>())).Returns(true);
        Mock<IContentModelService> models = new();
        models.Setup(m => m.GetFocusedModel()).Returns(fileSystem.Object);
        Mock<IPromptService> prompts = new();
        var dialog = new CsvViewerDialog(models.Object, Mock.Of<IFileToTextConverterService>(), Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(), new ThemeService(), prompts.Object) { Url = document };
        return new Fixture { Dialog = dialog, FileSystem = fileSystem, Prompts = prompts };
    }

    private static void AnswerName(Fixture fixture, string? name) =>
        fixture.Prompts.Setup(p => p.AskAsync(It.IsAny<TextPrompt<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(name);

    private static void AnswerOverwrite(Fixture fixture, bool? answer) =>
        fixture.Prompts.Setup(p => p.ConfirmAsync(It.IsAny<ConfirmationPrompt>(), It.IsAny<CancellationToken>())).ReturnsAsync(answer);

    /// <summary>Captures what the export wrote, as text, into the returned holder.</summary>
    private static Holder CaptureWrites(Fixture fixture)
    {
        var holder = new Holder();
        fixture.FileSystem.Setup(f => f.WriteFileAsync(It.IsAny<Url>(), It.IsAny<MemoryStream>()))
            .Callback<Url, MemoryStream>((url, stream) =>
            {
                holder.Target = url;
                holder.Text = Encoding.UTF8.GetString(stream.ToArray());
            })
            .Returns(Task.CompletedTask);
        return holder;
    }

    private sealed class Holder
    {
        public Url? Target { get; set; }
        public string? Text { get; set; }
    }

    [TestMethod]
    public async Task ExportWritesEveryRowOnScreenWhenNothingIsMarked()
    {
        Fixture fixture = CreateFixture(Sales, "name,amount\nA,1\nB,2\n");
        AnswerName(fixture, "out.csv");
        Holder written = CaptureWrites(fixture);
        await fixture.Dialog.OnInitializeAsync();

        await fixture.Dialog.ExportAsync();

        Assert.AreEqual("name,amount\r\nA,1\r\nB,2\r\n", written.Text);
        Assert.AreEqual("out.csv", written.Target?.Name);
        Assert.AreEqual("Exported 2 row(s) to out.csv", fixture.Dialog.Status);
    }

    [TestMethod]
    public async Task ExportWritesOnlyTheMarkedRowsInGridOrder()
    {
        Fixture fixture = CreateFixture(Sales, "name,amount\nA,1\nB,2\nC,3\n");
        AnswerName(fixture, "out.csv");
        Holder written = CaptureWrites(fixture);
        await fixture.Dialog.OnInitializeAsync();
        // Marks A and C: Space marks the row and moves down, so Down skips B. The grid takes the keys only when it has the focus.
        fixture.Dialog.Grid.SetFocus();
        await fixture.Dialog.Grid.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));
        await fixture.Dialog.Grid.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));
        await fixture.Dialog.Grid.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));

        await fixture.Dialog.ExportAsync();

        Assert.AreEqual("name,amount\r\nA,1\r\nC,3\r\n", written.Text);
    }

    [TestMethod]
    public async Task ExportOfATabFileUsesCommasAndQuotesWhatNeedsIt()
    {
        Fixture fixture = CreateFixture(Notes, "name\tnote\nA\tone, two\n");
        AnswerName(fixture, "out.csv");
        Holder written = CaptureWrites(fixture);
        await fixture.Dialog.OnInitializeAsync();

        await fixture.Dialog.ExportAsync();

        Assert.AreEqual("name,note\r\nA,\"one, two\"\r\n", written.Text);
    }

    [TestMethod]
    public async Task ExportDefaultsTheNameToTheFileStemWithExportAndCsv()
    {
        Fixture fixture = CreateFixture(Sales, "name\nA\n");
        await fixture.Dialog.OnInitializeAsync();

        await fixture.Dialog.ExportAsync();

        fixture.Prompts.Verify(p => p.AskAsync(
            It.Is<TextPrompt<string>>(prompt => prompt.DefaultValue == "sales-export.csv"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AnExistingFileIsReplacedOnlyAfterYes()
    {
        Fixture fixture = CreateFixture(Sales, "name\nA\n");
        fixture.FileSystem.Setup(f => f.ExistsFile(It.IsAny<Url>())).Returns(true);
        AnswerName(fixture, "out.csv");
        AnswerOverwrite(fixture, true);
        await fixture.Dialog.OnInitializeAsync();

        await fixture.Dialog.ExportAsync();

        fixture.FileSystem.Verify(f => f.WriteFileAsync(It.IsAny<Url>(), It.IsAny<MemoryStream>()), Times.Once);
    }

    [TestMethod]
    public async Task AnExistingFileIsNotWrittenOnNo()
    {
        Fixture fixture = CreateFixture(Sales, "name\nA\n");
        fixture.FileSystem.Setup(f => f.ExistsFile(It.IsAny<Url>())).Returns(true);
        AnswerName(fixture, "out.csv");
        AnswerOverwrite(fixture, false);
        await fixture.Dialog.OnInitializeAsync();

        await fixture.Dialog.ExportAsync();

        fixture.FileSystem.Verify(f => f.WriteFileAsync(It.IsAny<Url>(), It.IsAny<MemoryStream>()), Times.Never);
        Assert.AreEqual("Nothing was written", fixture.Dialog.Status);
    }

    [TestMethod]
    public async Task AnExistingFileIsNotWrittenWhenTheQuestionIsCancelled()
    {
        Fixture fixture = CreateFixture(Sales, "name\nA\n");
        fixture.FileSystem.Setup(f => f.ExistsFile(It.IsAny<Url>())).Returns(true);
        AnswerName(fixture, "out.csv");
        AnswerOverwrite(fixture, null);
        await fixture.Dialog.OnInitializeAsync();

        await fixture.Dialog.ExportAsync();

        fixture.FileSystem.Verify(f => f.WriteFileAsync(It.IsAny<Url>(), It.IsAny<MemoryStream>()), Times.Never);
        Assert.AreEqual("Nothing was written", fixture.Dialog.Status);
    }

    [TestMethod]
    public async Task ACancelledNamePromptWritesNothing()
    {
        Fixture fixture = CreateFixture(Sales, "name\nA\n");
        AnswerName(fixture, null);
        await fixture.Dialog.OnInitializeAsync();

        await fixture.Dialog.ExportAsync();

        fixture.FileSystem.Verify(f => f.WriteFileAsync(It.IsAny<Url>(), It.IsAny<MemoryStream>()), Times.Never);
        Assert.AreEqual("Nothing was written", fixture.Dialog.Status);
    }

    [TestMethod]
    public async Task ANameWithAFolderInItWritesNothing()
    {
        Fixture fixture = CreateFixture(Sales, "name\nA\n");
        AnswerName(fixture, "sub/out.csv");
        await fixture.Dialog.OnInitializeAsync();

        await fixture.Dialog.ExportAsync();

        fixture.FileSystem.Verify(f => f.WriteFileAsync(It.IsAny<Url>(), It.IsAny<MemoryStream>()), Times.Never);
        StringAssert.Contains(fixture.Dialog.Status, "no folder");
    }

    [TestMethod]
    public async Task AReadOnlyFolderWritesNothing()
    {
        Fixture fixture = CreateFixture(Sales, "name\nA\n");
        fixture.FileSystem.SetupGet(f => f.IsReadOnly).Returns(true);
        AnswerName(fixture, "out.csv");
        await fixture.Dialog.OnInitializeAsync();

        await fixture.Dialog.ExportAsync();

        fixture.Prompts.Verify(p => p.AskAsync(It.IsAny<TextPrompt<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.FileSystem.Verify(f => f.WriteFileAsync(It.IsAny<Url>(), It.IsAny<MemoryStream>()), Times.Never);
        StringAssert.Contains(fixture.Dialog.Status, "read-only");
    }

    [TestMethod]
    public async Task F2AsksForTheFileNameOnTheGrid()
    {
        Fixture fixture = CreateFixture(Sales, "name\nA\n");
        AnswerName(fixture, null);
        await fixture.Dialog.OnInitializeAsync();

        bool handled = await fixture.Dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F2, false, false, false));

        Assert.IsTrue(handled);
        fixture.Prompts.Verify(p => p.AskAsync(It.IsAny<TextPrompt<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iConsole.Formats.Services;
using Moq;

namespace iConsole.Test.Dialogs;

[TestClass]
public class FileViewerDialogPdfSignaturesTest
{
    private static readonly string[] Text = ["first line", "second line", "third line"];
    private static readonly string[] Section = ["── Digital signatures ──", "  Signature 1", string.Empty];

    private sealed class Converter : IFileToTextConverterService
    {
        /// <summary>The routing answers come from the real converter, so the viewer shows what it shows in the app.</summary>
        private static readonly FileToTextConverterService Routing = new(new iConsole.Formats.Xps.XpsDocumentReader());

        public bool IsEncodingRelevant(Url? url) => Routing.IsEncodingRelevant(url);

        public bool HasDisassembly(Url? url) => Routing.HasDisassembly(url);

        public bool HasPdbSummary(Url? url) => Routing.HasPdbSummary(url);

        public bool HasPdfSignatures(Url? url) => Routing.HasPdfSignatures(url);

        public TaskCompletionSource<IReadOnlyList<string>> Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }

        public CancellationToken Token { get; private set; }

        public bool ReturnNullTask { get; set; }

        public Task ReadPdbForViewerAsync(Url url, IFileSystem fileSystem, ITextModel textModel) =>
            ReadFileAsync(url, fileSystem, textModel);

        public Task ReadFileAsync(Url? url, IFileSystem fileSystem, ITextModel textModel)
        {
            textModel.Lines.Clear();
            foreach (string line in Text)
            {
                textModel.Lines.Add(line);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> BuildDisassemblyAsync(Url? url, IFileSystem fileSystem, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> BuildPdfSignatureLinesAsync(Url? url, IFileSystem fileSystem, CancellationToken cancellationToken)
        {
            Calls++;
            Token = cancellationToken;
            Started.TrySetResult(true);
            return ReturnNullTask ? null! : Gate.Task;
        }
    }

    private static FileViewerDialog CreateDialog(Converter converter, string url = "file://C:/x/doc.pdf")
    {
        Mock<IContentModel> fileSystem = new();
        Mock<IContentModelService> models = new();
        models.Setup(m => m.GetFocusedModel()).Returns(fileSystem.Object);
        return new FileViewerDialog(
            models.Object,
            converter,
            Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(),
            new ThemeService())
        { Url = new Url(url) };
    }

    private static iConsole.Controls.Viewers.TextViewerControl Viewer(FileViewerDialog dialog) =>
        dialog.Controls.OfType<iConsole.Controls.Viewers.TextViewerControl>().Single();

    private static string[] Lines(FileViewerDialog dialog) => [.. Viewer(dialog).TextModel.Lines];

    private static async Task LandAsync(FileViewerDialog dialog)
    {
        try
        {
            await dialog.PdfSignaturesTask!;
        }
        catch
        {
            // A failed check is reported by OnIdle, which is what is under test.
        }

        dialog.OnIdle();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheTextIsShownFirst_ThenTheSectionIsPutAboveIt()
    {
        Converter converter = new();
        FileViewerDialog dialog = CreateDialog(converter);

        await dialog.OnInitializeAsync();
        CollectionAssert.AreEqual(Text, Lines(dialog));

        converter.Gate.SetResult(Section);
        await LandAsync(dialog);

        CollectionAssert.AreEqual(Section.Concat(Text).ToArray(), Lines(dialog));
        Assert.IsNull(dialog.PdfSignaturesTask);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AReaderWhoHasScrolledDown_KeepsTheirPlaceInTheText()
    {
        Converter converter = new();
        FileViewerDialog dialog = CreateDialog(converter);
        await dialog.OnInitializeAsync();
        Viewer(dialog).ScrollableModel.ScrollPosition.Y = 1;
        Viewer(dialog).ScrollableModel.CursorPosition.Y = 2;

        converter.Gate.SetResult(Section);
        await LandAsync(dialog);

        Assert.AreEqual(1 + Section.Length, Viewer(dialog).ScrollableModel.ScrollPosition.Y);
        Assert.AreEqual(2 + Section.Length, Viewer(dialog).ScrollableModel.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AReaderAtTheTop_SeesTheSection()
    {
        Converter converter = new();
        FileViewerDialog dialog = CreateDialog(converter);
        await dialog.OnInitializeAsync();

        converter.Gate.SetResult(Section);
        await LandAsync(dialog);

        Assert.AreEqual(0, Viewer(dialog).ScrollableModel.ScrollPosition.Y);
        Assert.AreEqual(0, Viewer(dialog).ScrollableModel.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClosingBeforeItLands_CancelsTheCheckAndAddsNothing()
    {
        Converter converter = new();
        FileViewerDialog dialog = CreateDialog(converter);
        await dialog.OnInitializeAsync();
        Task pending = dialog.PdfSignaturesTask!;
        await converter.Started.Task;

        dialog.Close();
        Assert.IsTrue(converter.Token.IsCancellationRequested, "Closing must cancel the signature check.");
        converter.Gate.SetResult(Section);
        await pending;
        dialog.OnIdle();

        CollectionAssert.AreEqual(Text, Lines(dialog));
        Assert.IsNull(dialog.PdfSignaturesTask);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AResultForAnotherFile_IsDropped()
    {
        Converter converter = new();
        FileViewerDialog dialog = CreateDialog(converter);
        await dialog.OnInitializeAsync();

        dialog.Url = new Url("file://C:/x/other.pdf");
        converter.Gate.SetResult(Section);
        await LandAsync(dialog);

        CollectionAssert.AreEqual(Text, Lines(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedCheck_LeavesTheTextAsItWas()
    {
        Converter converter = new();
        FileViewerDialog dialog = CreateDialog(converter);
        await dialog.OnInitializeAsync();

        converter.Gate.SetException(new InvalidOperationException("boom"));
        await LandAsync(dialog);

        CollectionAssert.AreEqual(Text, Lines(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptyResult_LeavesTheTextAsItWas()
    {
        Converter converter = new();
        FileViewerDialog dialog = CreateDialog(converter);
        await dialog.OnInitializeAsync();

        converter.Gate.SetResult([]);
        await LandAsync(dialog);

        CollectionAssert.AreEqual(Text, Lines(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ANullTask_IsTolerated()
    {
        Converter converter = new() { ReturnNullTask = true };
        FileViewerDialog dialog = CreateDialog(converter);

        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        CollectionAssert.AreEqual(Text, Lines(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileThatIsNotAPdf_StartsNoCheck()
    {
        Converter converter = new();
        FileViewerDialog dialog = CreateDialog(converter, "file://C:/x/readme.txt");

        await dialog.OnInitializeAsync();

        Assert.AreEqual(0, converter.Calls);
        Assert.IsNull(dialog.PdfSignaturesTask);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMockedConverterThatReturnsNothing_IsTolerated()
    {
        // A loose mock answers a task with a null result; the text must stay as it is.
        Mock<IFileToTextConverterService> mock = new();
        mock.Setup(m => m.ReadFileAsync(It.IsAny<Url?>(), It.IsAny<IFileSystem>(), It.IsAny<ITextModel>()))
            .Callback<Url?, IFileSystem, ITextModel>((_, _, model) => model.Lines.Add("only line"))
            .Returns(Task.CompletedTask);
        Mock<IContentModel> fileSystem = new();
        Mock<IContentModelService> models = new();
        models.Setup(m => m.GetFocusedModel()).Returns(fileSystem.Object);
        FileViewerDialog dialog = new(models.Object, mock.Object, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), new ThemeService())
        { Url = new Url("file://C:/x/doc.pdf") };

        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        CollectionAssert.AreEqual(new[] { "only line" }, Lines(dialog));
    }
}

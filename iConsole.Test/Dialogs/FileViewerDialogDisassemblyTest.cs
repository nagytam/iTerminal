using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Dialogs;

[TestClass]
public class FileViewerDialogDisassemblyTest
{
    private static readonly string[] Metadata = ["── File ──", "  Name  app.dll"];

    /// <summary>A converter whose disassembly stays pending until the test completes it.</summary>
    private sealed class GatedConverter : IFileToTextConverterService
    {
        public TaskCompletionSource<IReadOnlyList<string>> Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisassemblyCalls { get; private set; }

        public CancellationToken Token { get; private set; }

        public bool ReturnNullTask { get; set; }

        public Task ReadFileAsync(Url? url, IFileSystem fileSystem, ITextModel textModel)
        {
            textModel.Lines.Clear();
            foreach (string line in Metadata)
            {
                textModel.Lines.Add(line);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> BuildDisassemblyAsync(Url? url, IFileSystem fileSystem, CancellationToken cancellationToken)
        {
            DisassemblyCalls++;
            Token = cancellationToken;
            return ReturnNullTask ? null! : Gate.Task;
        }
    }

    private static FileViewerDialog CreateDialog(GatedConverter converter, string url = "file://C:/x/app.dll")
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

    private static string[] Lines(FileViewerDialog dialog) => [.. ((ITextModel)GetModel(dialog)).Lines];

    private static ITextModel GetModel(FileViewerDialog dialog) =>
        dialog.Controls.OfType<iConsole.Controls.Viewers.TextViewerControl>().Single().TextModel;

    private static async Task LandAsync(FileViewerDialog dialog)
    {
        Task task = dialog.DisassemblyTask!;
        try
        {
            await task;
        }
        catch
        {
            // A failed worker is reported by OnIdle, which is what is under test.
        }

        dialog.OnIdle();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MetadataPageIsShownFirst_ThenTheDisassemblyReplacesTheProgressLine()
    {
        GatedConverter converter = new();
        FileViewerDialog dialog = CreateDialog(converter);

        await dialog.OnInitializeAsync();

        CollectionAssert.AreEqual(new[] { "── File ──", "  Name  app.dll", string.Empty, "Disassembling…" }, Lines(dialog));
        Assert.IsFalse(dialog.DisassemblyTask!.IsCompleted);

        converter.Gate.SetResult(["── Disassembly (IL) ──", "  .method A::B"]);
        await LandAsync(dialog);

        CollectionAssert.AreEqual(
            new[] { "── File ──", "  Name  app.dll", string.Empty, "── Disassembly (IL) ──", "  .method A::B" },
            Lines(dialog));
        Assert.IsNull(dialog.DisassemblyTask);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClosingBeforeItLands_CancelsTheWorkAndAppliesNothing()
    {
        GatedConverter converter = new();
        FileViewerDialog dialog = CreateDialog(converter);
        await dialog.OnInitializeAsync();
        Task pending = dialog.DisassemblyTask!;

        dialog.Close();
        Assert.IsTrue(converter.Token.IsCancellationRequested, "Closing must cancel the disassembly.");
        converter.Gate.SetResult(["── Disassembly (IL) ──"]);
        await pending;
        dialog.OnIdle();

        Assert.IsFalse(Lines(dialog).Any(l => l.Contains("Disassembly (IL)")));
        Assert.IsNull(dialog.DisassemblyTask);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AResultForAnotherFile_IsDropped()
    {
        GatedConverter converter = new();
        FileViewerDialog dialog = CreateDialog(converter);
        await dialog.OnInitializeAsync();

        dialog.Url = new Url("file://C:/x/other.dll");
        converter.Gate.SetResult(["── Disassembly (IL) ──"]);
        await LandAsync(dialog);

        Assert.IsFalse(Lines(dialog).Any(l => l.Contains("Disassembly (IL)")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ANullTask_IsToleratedAndTheProgressLineGoes()
    {
        GatedConverter converter = new() { ReturnNullTask = true };
        FileViewerDialog dialog = CreateDialog(converter);

        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        CollectionAssert.AreEqual(Metadata, Lines(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptyResult_LeavesOnlyTheMetadata()
    {
        GatedConverter converter = new();
        FileViewerDialog dialog = CreateDialog(converter);
        await dialog.OnInitializeAsync();

        converter.Gate.SetResult([]);
        await LandAsync(dialog);

        CollectionAssert.AreEqual(Metadata, Lines(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedDisassembly_IsReportedUnderTheMetadata()
    {
        GatedConverter converter = new();
        FileViewerDialog dialog = CreateDialog(converter);
        await dialog.OnInitializeAsync();

        converter.Gate.SetException(new InvalidOperationException("boom"));
        await LandAsync(dialog);

        string[] lines = Lines(dialog);
        Assert.IsTrue(lines.Any(l => l.Contains("Disassembly failed: boom")), string.Join('\n', lines));
        Assert.AreEqual("── File ──", lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileThatIsNotABinary_StartsNoDisassembly()
    {
        GatedConverter converter = new();
        FileViewerDialog dialog = CreateDialog(converter, "file://C:/x/readme.txt");

        await dialog.OnInitializeAsync();

        Assert.AreEqual(0, converter.DisassemblyCalls);
        Assert.IsNull(dialog.DisassemblyTask);
        CollectionAssert.AreEqual(Metadata, Lines(dialog));
    }
}

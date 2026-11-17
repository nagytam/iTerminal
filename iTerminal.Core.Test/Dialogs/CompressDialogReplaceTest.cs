using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Test.Helper;
using Moq;
using iConsole.Services;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>
/// Alt+F5 for the formats that replace their archive (GZIP, BZIP2, XZ, TAR.XZ): one file only, a
/// y/n question before an existing archive is written over, and only y writes.
/// </summary>
[TestClass]
public class CompressDialogReplaceTest
{
    private string _root = string.Empty;
    private string Target => Path.Combine(_root, "dst");

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "CompressDialogReplaceTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Target);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private (CompressDialog Dialog, MarkableTestModel Model) Create()
    {
        var model = MarkableTestModel.Create(Path.Combine(_root, "src"));
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetFocusedModel()).Returns(model);
        service.Setup(s => s.GetLastContentModel()).Returns(model);
        service.SetupGet(s => s.NonFocusedFolderUrl).Returns(new Url(Target + Path.DirectorySeparatorChar));
        var compression = new ArchiveCompressionService(new FakeProcessRunner(), new SevenZipArchiveWriter(new FakeProcessRunner(), () => "7z"));
        var dialog = new CompressDialog(service.Object, compression, null!, null!, Mock.Of<IBeepService>(),
            new DateTimeProviderService(), TestTheme(), null!, null!, null!, null!)
        {
            SourceModel = model,
        };
        return (dialog, model);
    }

    private static KeyEvent Key(char c) => new(c, char.ToUpperInvariant(c) is var upper && upper >= 'A' && upper <= 'Z' ? (ConsoleKey)upper : ConsoleKey.NoName, false, false, false);

    [Timeout(60_000)]
    [TestMethod]
    public async Task OneFileFormatRefusesSeveralMarkedRows()
    {
        var (dialog, model) = Create();
        model.SetMarks("*", marked: true, includeFolders: true);
        await ShowAsync(dialog);

        dialog.Format = ArchiveFormat.Gzip;
        dialog.ArchivePath = Path.Combine(Target, "x.gz");

        var (options, error) = dialog.ReadOptions();
        Assert.IsNull(options);
        StringAssert.Contains(error!, "GZIP holds one file");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheReplaceBoxIsDisabledForTheFormatsThatReplace()
    {
        var (dialog, model) = Create();
        model.CursorPosition.Y = 2;
        await ShowAsync(dialog);

        dialog.Format = ArchiveFormat.Zip;
        Assert.IsTrue(dialog.IsReplaceExistingEnabledForTest);

        foreach (var format in new[] { ArchiveFormat.Gzip, ArchiveFormat.Bzip2, ArchiveFormat.Xz, ArchiveFormat.TarXz })
        {
            dialog.Format = format;
            Assert.IsFalse(dialog.IsReplaceExistingEnabledForTest, format.Name);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheHintSaysWhetherTheExistingArchiveIsAddedToOrReplaced()
    {
        var (dialog, model) = Create();
        model.CursorPosition.Y = 2;
        await ShowAsync(dialog);
        File.WriteAllText(Path.Combine(Target, "a.gz"), "old");

        dialog.Format = ArchiveFormat.Gzip;
        dialog.ArchivePath = Path.Combine(Target, "a.gz");
        StringAssert.Contains(dialog.HintForTest, "Replaces the existing archive.");
        Assert.IsFalse(dialog.HintForTest.Contains("Adds to the existing archive."));

        // The hint is worked out when the format is chosen, so the name is set first.
        File.WriteAllText(Path.Combine(Target, "a.zip"), "old");
        dialog.ArchivePath = Path.Combine(Target, "a.zip");
        dialog.Format = ArchiveFormat.Zip;
        StringAssert.Contains(dialog.HintForTest, "Adds to the existing archive.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnlyYWritesOverAnExistingArchive()
    {
        var (dialog, model) = Create();
        model.CursorPosition.Y = 2;
        await ShowAsync(dialog);
        dialog.Format = ArchiveFormat.Gzip;
        dialog.ArchivePath = Path.Combine(Target, "a.gz");
        var written = 0;
        Func<Task> proceed = () => { written++; return Task.CompletedTask; };

        dialog.AskToReplace(dialog.ArchivePath, proceed);
        Assert.AreEqual("Replace a.gz? (y/n)", dialog.ReplaceQuestionForTest);
        Assert.IsTrue(dialog.ReplaceQuestionForTest.Length > 0);

        await dialog.HandleKeyEventAsync(Key('x'));
        Assert.AreEqual(0, written, "any other key cancels");
        Assert.AreEqual(string.Empty, dialog.ReplaceQuestionForTest);

        dialog.AskToReplace(dialog.ArchivePath, proceed);
        await dialog.HandleKeyEventAsync(Key('y'));
        Assert.AreEqual(1, written, "y writes");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheYOfTheQuestionIsNotTypedIntoTheName()
    {
        var (dialog, model) = Create();
        model.CursorPosition.Y = 2;
        await ShowAsync(dialog);
        dialog.Format = ArchiveFormat.Gzip;
        dialog.ArchivePath = Path.Combine(Target, "a.gz");
        dialog.AskToReplace(dialog.ArchivePath, () => Task.CompletedTask);

        await dialog.HandleKeyEventAsync(Key('y'));

        StringAssert.EndsWith(dialog.ArchivePath, "a.gz", "the name is unchanged");
    }
}

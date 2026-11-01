using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Test.Helper;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>Shift+F5: which rows are archives, where the files land, and when the key is offered at all.</summary>
[TestClass]
public class ExtractDialogTest
{
    private string _root = string.Empty;
    private string Target => Path.Combine(_root, "dst");

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "ExtractDialogTest_" + Guid.NewGuid().ToString("N")[..8]);
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

    private static ArchiveExtractionService Service(bool sevenZip = false) => new(new FakeProcessRunner(), () => sevenZip ? "7z" : null);

    /// <summary>The test folder's listing plus two archives: a.zip and b.rar.</summary>
    private (ExtractDialog Dialog, MarkableTestModel Model, IContentModelService Service) Create(bool sevenZip = false)
    {
        var model = MarkableTestModel.Create(Path.Combine(_root, "src"));
        model.AddFile("a.zip", "zip");
        model.AddFile("b.rar", "rar");

        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetFocusedModel()).Returns(model);
        service.Setup(s => s.GetLastContentModel()).Returns(model);
        service.SetupGet(s => s.NonFocusedFolderUrl).Returns(new Url(Target + Path.DirectorySeparatorChar));
        var dialog = new ExtractDialog(service.Object, Service(sevenZip), null!, null!, Mock.Of<IBeepService>(),
            new DateTimeProviderService(), TestTheme(), null!, null!, null!, null!)
        {
            SourceModel = model,
        };
        return (dialog, model, service.Object);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheArchiveUnderTheCursorGoesIntoAFolderOfItsOwnInTheOtherPanel()
    {
        var (dialog, model, _) = Create();
        model.CursorPosition.Y = model.FilteredEntries.ToList().FindIndex(e => e.DisplayName == "a.zip");

        await ShowAsync(dialog);

        Assert.AreEqual("Extract a.zip", dialog.Title);
        StringAssert.Contains(dialog.TargetFolder, "dst");
        StringAssert.EndsWith(dialog.LandingForTest.TrimEnd('/', '\\'), Path.Combine("dst", "a"));
        Assert.AreEqual(string.Empty, dialog.ErrorMessage.Text);
        AssertScreenContains(Render(dialog), "Each archive into a folder of its own");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnlyTheMarkedArchivesAreTakenAndAMissingToolIsSaidUpFront()
    {
        var (dialog, model, _) = Create();
        model.SetMarks("*", marked: true, includeFolders: true);

        await ShowAsync(dialog);

        Assert.AreEqual("Extract 2 archives", dialog.Title, "the folder and the plain files are not archives");
        StringAssert.Contains(dialog.SourceLabelForTest, "a.zip, b.rar");
        Assert.AreEqual(".rar archives need 7-Zip installed.", dialog.ErrorMessage.Text);
        StringAssert.Contains(dialog.LandingForTest, "a folder per archive");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TypingWithdrawsAnErrorButNotTheNoticeAboutAMissingTool()
    {
        var (dialog, model, _) = Create();
        model.SetMarks("*", marked: true, includeFolders: true);
        await ShowAsync(dialog);
        Assert.AreEqual(".rar archives need 7-Zip installed.", dialog.ErrorMessage.Text);

        dialog.ErrorMessage.Text = "The disk is full.";
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        Assert.AreEqual("The disk is full.", dialog.ErrorMessage.Text, "moving on does not clear it");

        await dialog.HandleKeyEventAsync(Char('x'));
        Assert.AreEqual(".rar archives need 7-Zip installed.", dialog.ErrorMessage.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TypingClearsAnErrorWhenNothingWasBlocked()
    {
        var (dialog, model, _) = Create();
        model.CursorPosition.Y = model.FilteredEntries.ToList().FindIndex(e => e.DisplayName == "a.zip");
        await ShowAsync(dialog);
        dialog.ErrorMessage.Text = "The disk is full.";

        await dialog.HandleKeyEventAsync(Char('x'));

        Assert.AreEqual(string.Empty, dialog.ErrorMessage.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutOwnFoldersTheFilesLandInTheTargetItself()
    {
        var (dialog, model, _) = Create();
        model.CursorPosition.Y = model.FilteredEntries.ToList().FindIndex(e => e.DisplayName == "a.zip");
        await ShowAsync(dialog);

        dialog.EachIntoOwnFolder = false;

        StringAssert.EndsWith(dialog.LandingForTest.TrimEnd('/', '\\'), "dst");
        Assert.AreEqual(new ExtractionOptions(null, false), dialog.ReadOptions().Options);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ShiftF5IsOfferedOnlyOverAnArchive()
    {
        var (dialog, model, service) = Create();
        var command = new ExtractDialogCommand(dialog, service);

        Assert.AreEqual(ConsoleKey.F5, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Shift);
        model.CursorPosition.Y = model.FilteredEntries.ToList().FindIndex(e => e.DisplayName == "a.txt");
        Assert.IsFalse(command.CanExecute(), "a.txt is not an archive");
        model.CursorPosition.Y = model.FilteredEntries.ToList().FindIndex(e => e.DisplayName == "b.rar");
        Assert.IsTrue(command.CanExecute(), "offered even without 7-Zip: the dialog then says why");
    }
}

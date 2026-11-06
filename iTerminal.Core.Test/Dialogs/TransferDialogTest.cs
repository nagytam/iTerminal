using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>
/// The Copy/Move dialogs share everything in <see cref="TransferDialog"/>; these tests drive it
/// through <see cref="CopyDialog"/> and <see cref="MoveDialog"/>.
/// </summary>
[TestClass]
public class TransferDialogTest
{
    private string _root = string.Empty;
    private string Source => Path.Combine(_root, "src", "report.txt");
    private string TargetFolder => Path.Combine(_root, "dst");

    [TestInitialize]
    public void Initialize()
    {
        // Real folders: a URL is only treated as a folder (and a bare name combined onto it) when it
        // is one on disk, which is what the panels always hand the dialog.
        _root = Path.Combine(Path.GetTempPath(), "TransferDialogTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        Directory.CreateDirectory(Path.Combine(_root, "dst"));
        File.WriteAllText(Source, "x");
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

    private Mock<IContentModelService> ContentModels()
    {
        var service = new Mock<IContentModelService>();
        service.SetupGet(s => s.Url).Returns(new Url(Path.GetDirectoryName(Source)!));
        service.SetupGet(s => s.FocusedFileUrl).Returns(new Url(Source));
        service.SetupGet(s => s.NonFocusedFolderUrl).Returns(new Url(TargetFolder));
        return service;
    }

    private static CopyDialog Copy(IContentModelService contentModelService)
    {
        var dateTime = new Mock<IDateTimeProviderService>();
        dateTime.SetupGet(d => d.Now).Returns(DateTime.Now);
        return new CopyDialog(null!, contentModelService, null!, null!, null!, new Mock<IBeepService>().Object, dateTime.Object, TestTheme(), null!, null!, null!, null!);
    }

    private static MoveDialog Move(IContentModelService contentModelService)
    {
        var dateTime = new Mock<IDateTimeProviderService>();
        dateTime.SetupGet(d => d.Now).Returns(DateTime.Now);
        return new MoveDialog(null!, contentModelService, null!, null!, null!, new Mock<IBeepService>().Object, dateTime.Object, TestTheme(), null!, null!, null!, null!);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpensWithTheSourceAndTheOtherPanelsFolderAsTarget()
    {
        var dialog = Copy(ContentModels().Object);
        await ShowAsync(dialog);

        Assert.AreEqual("Copy file", dialog.Title);
        StringAssert.EndsWith(dialog.SourceUrl.FullUrl, "report.txt");
        StringAssert.EndsWith(dialog.TextBoxTarget.Text, "report.txt");
        StringAssert.Contains(dialog.TextBoxTarget.Text, "dst");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheTargetBoxIsLabelledAsADestination()
    {
        // It was labelled "Name", a leftover from the create dialogs that read as "rename to".
        var dialog = Copy(ContentModels().Object);
        await ShowAsync(dialog);

        Assert.AreEqual("To", dialog.TextBoxTarget.Label);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheResolvedTargetFollowsTheBoxAsItIsEdited()
    {
        // The "Target:" line was computed once when the dialog opened and never again.
        var dialog = Copy(ContentModels().Object);
        await ShowAsync(dialog);

        dialog.TextBoxTarget.Text = "renamed.txt";

        StringAssert.Contains(dialog.TargetPreviewForTest, "renamed.txt");
        StringAssert.Contains(dialog.TargetPreviewForTest, "src", "a bare name lands next to the source");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheDialogIsSizedToItsContent()
    {
        var dialog = Copy(ContentModels().Object);
        await ShowAsync(dialog);

        Assert.AreEqual(TransferDialog.DialogHeight, (int)dialog.ViewSize.Height);
        AssertScreenContains(Render(dialog), "Overwrite");
    }

    private CopyDialog CopyWith(Mock<ISettingsService> settings)
    {
        var dateTime = new Mock<IDateTimeProviderService>();
        dateTime.SetupGet(d => d.Now).Returns(DateTime.Now);
        return new CopyDialog(null!, ContentModels().Object, null!, null!, null!, new Mock<IBeepService>().Object, dateTime.Object, TestTheme(), null!, null!, null!, null!, settings.Object);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task VerifyAfterCopy_IsOffByDefault_AndShown()
    {
        var dialog = Copy(ContentModels().Object);
        await ShowAsync(dialog);

        Assert.IsFalse(dialog.VerifyAfterCopy);
        AssertScreenContains(Render(dialog), "Verify after copy");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task VerifyAfterCopy_IsReadFromTheSettingEachTimeTheDialogOpens()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetAsBool(iTerminal.Core.Data.Settings.TransferVerifyAfterCopy, It.IsAny<bool?>())).Returns(true);
        var dialog = CopyWith(settings);
        await ShowAsync(dialog);
        Assert.IsTrue(dialog.VerifyAfterCopy);

        settings.Setup(s => s.GetAsBool(iTerminal.Core.Data.Settings.TransferVerifyAfterCopy, It.IsAny<bool?>())).Returns(false);
        await dialog.OnInitializeAsync();
        Assert.IsFalse(dialog.VerifyAfterCopy, "the dialog is created once; the setting is read when it opens");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task VerifyAfterCopy_IsRememberedWhenTheTransferStarts()
    {
        var settings = new Mock<ISettingsService>();
        var dialog = CopyWith(settings);
        await ShowAsync(dialog);

        dialog.VerifyAfterCopy = true;
        dialog.RememberVerifyChoice();

        settings.Verify(s => s.Set(iTerminal.Core.Data.Settings.TransferVerifyAfterCopy, "True"), Times.Once);
        Assert.IsFalse(iTerminal.Core.Data.Settings.TransferVerifyAfterCopy.StartsWith("__"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheTargetBoxScrollsWithinItsOwnWidth()
    {
        var dialog = Copy(ContentModels().Object);
        await ShowAsync(dialog);

        Assert.IsTrue(dialog.TextBoxTarget.ViewSize.Width > 0);
        Assert.AreEqual(dialog.TextBoxTarget.EditableWidth, (int)dialog.TextBoxTarget.ScrollableModel.ViewSize.Width,
            "the box scrolls within the width its caption leaves");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MoveUsesTheSameLayoutWithItsOwnTitle()
    {
        var dialog = Move(ContentModels().Object);
        await ShowAsync(dialog);

        Assert.AreEqual("Move file", dialog.Title);
        Assert.AreEqual("To", dialog.TextBoxTarget.Label);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscapeClosesTheDialog()
    {
        var dialog = Copy(ContentModels().Object);
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }
}

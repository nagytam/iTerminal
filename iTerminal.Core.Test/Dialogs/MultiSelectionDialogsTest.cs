using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Test.Helper;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>F5, Shift+F6 and F8 with rows marked: the dialogs take the marks, not the cursor row.</summary>
[TestClass]
public class MultiSelectionDialogsTest
{
    private string _root = string.Empty;
    private string Source => Path.Combine(_root, "src");
    private string Target => Path.Combine(_root, "dst");

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "MultiSelectionDialogsTest_" + Guid.NewGuid().ToString("N")[..8]);
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

    private Mock<IContentModelService> Service(IContentModel model)
    {
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetFocusedModel()).Returns(model);
        service.Setup(s => s.GetLastContentModel()).Returns(model);
        service.SetupGet(s => s.Url).Returns(model.Url);
        service.SetupGet(s => s.FocusedFileUrl).Returns(() => model.GetFocusedEntry()?.Url);
        service.SetupGet(s => s.NonFocusedFolderUrl).Returns(new Url(Target + Path.DirectorySeparatorChar));
        return service;
    }

    private static CopyDialog Copy(IContentModelService service) =>
        new(null!, service, null!, null!, null!, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme(), null!, null!, null!, null!);

    private static DeleteDialog Delete(IContentModelService service) =>
        new(service, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme(), null!, null!, null!, null!, null!, null!, null!);

    [TestMethod]
    public void TheMarksAreWhatAnOperationActsOn()
    {
        var model = MarkableTestModel.Create(Source);
        model.CursorPosition.Y = 4; // c.jpg, not marked
        model.SetMark(model.Row("a.txt"), true);
        model.SetMark(model.Row("photos"), true);

        var urls = FocusedEntry.ActionableUrls(model);

        CollectionAssert.AreEqual(new[] { "photos", "a.txt" }, urls.Select(FocusedEntry.NameOf).ToArray());
    }

    [TestMethod]
    public void WithoutMarksTheCursorRowIsWhatAnOperationActsOn()
    {
        var model = MarkableTestModel.Create(Source);
        model.CursorPosition.Y = 4;

        var urls = FocusedEntry.ActionableUrls(model);

        Assert.AreEqual("c.jpg", urls.Single().Name);
    }

    [TestMethod]
    public async Task CopyOpensOnTheMarkedRowsWithTheOtherPanelsFolderAsTarget()
    {
        var model = MarkableTestModel.Create(Source);
        model.SetMark(model.Row("a.txt"), true);
        model.SetMark(model.Row("b.txt"), true);
        var dialog = Copy(Service(model).Object);
        dialog.SourceModel = model;

        await ShowAsync(dialog);

        Assert.IsTrue(dialog.IsMultiple);
        Assert.AreEqual("Copy 2 items", dialog.Title);
        StringAssert.Contains(dialog.TextBoxTarget.Text, Target);
        Assert.IsFalse(dialog.TextBoxTarget.Text.Contains("a.txt"), "the box names the folder, not a file");
        StringAssert.Contains(dialog.SourceLabelForTest, "2 files: a.txt, b.txt");
        Assert.AreEqual(Path.Combine(Target, "b.txt"), dialog.TargetUrlFor(new Url(Path.Combine(Source, "b.txt"))).FullName);
    }

    [TestMethod]
    public async Task AFolderLandsAsAFolderOfTheSameName()
    {
        var model = MarkableTestModel.Create(Source);
        model.SetMark(model.Row("photos"), true);
        model.SetMark(model.Row("c.jpg"), true);
        var dialog = Copy(Service(model).Object);
        dialog.SourceModel = model;
        await ShowAsync(dialog);

        var target = dialog.TargetUrlFor(new Url(Path.Combine(Source, "photos") + Path.DirectorySeparatorChar));

        StringAssert.EndsWith(target.FullUrl, Path.Combine(Target, "photos") + Path.DirectorySeparatorChar, $"under its own name — a folder URL's Name is empty: {target.FullUrl}");
        StringAssert.Contains(dialog.SourceLabelForTest, "1 file, 1 folder");
    }

    [TestMethod]
    public async Task OneMarkedRowIsCopiedLikeTheCursorRow()
    {
        // The cursor is elsewhere: the single mark still wins, and the dialog looks as it always did.
        var model = MarkableTestModel.Create(Source);
        model.CursorPosition.Y = 4;
        model.SetMark(model.Row("a.txt"), true);
        var dialog = Copy(Service(model).Object);
        dialog.SourceModel = model;

        await ShowAsync(dialog);

        Assert.IsFalse(dialog.IsMultiple);
        Assert.AreEqual("Copy file", dialog.Title);
        StringAssert.EndsWith(dialog.SourceUrl.FullUrl, "a.txt");
        StringAssert.EndsWith(dialog.TextBoxTarget.Text, "a.txt");
    }

    [TestMethod]
    public async Task DeleteOpensOnTheMarkedRows()
    {
        var model = MarkableTestModel.Create(Source);
        model.SetMark(model.Row("photos"), true);
        model.SetMark(model.Row("b.txt"), true);
        var dialog = Delete(Service(model).Object);
        dialog.SourceModel = model;

        await ShowAsync(dialog);

        Assert.IsTrue(dialog.IsMultiple);
        Assert.AreEqual("Delete 2 items", dialog.Title);
        Assert.IsTrue(dialog.Recursive, "a marked folder is deleted with its contents, as a single one is");
        StringAssert.Contains(dialog.Url, "photos, b.txt");
        AssertScreenContains(Render(dialog), "1 file, 1 folder");
    }

    [TestMethod]
    public async Task DeletingOnlyFilesOffersNoRecursion()
    {
        var model = MarkableTestModel.Create(Source);
        model.SetMarks("*.txt", marked: true, includeFolders: false);
        var dialog = Delete(Service(model).Object);
        dialog.SourceModel = model;

        await ShowAsync(dialog);

        Assert.IsFalse(dialog.Recursive);
    }

    [TestMethod]
    public void CopyMoveAndDeleteAreOfferedOnTheParentRowWhenRowsAreMarked()
    {
        var model = MarkableTestModel.Create(Source);
        model.CursorPosition.Y = 0; // ".."
        var service = Service(model).Object;
        var copy = new CopyDialogCommand(Copy(service), service);
        var delete = new DeleteDialogCommand(Delete(service), service);

        Assert.IsFalse(copy.CanExecute(), "nothing marked, and the cursor is on ..");
        model.SetMark(model.Row("a.txt"), true);

        Assert.IsTrue(copy.CanExecute());
        Assert.IsTrue(delete.CanExecute());
    }
}

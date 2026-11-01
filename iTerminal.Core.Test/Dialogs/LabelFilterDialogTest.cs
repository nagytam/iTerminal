using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Labels;
using iTerminal.Core.Test.Helper;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class LabelFilterDialogTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "LabelFilterDialogTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_root, recursive: true);
    }

    private static IFileLabelService Labels(string redFile)
    {
        Mock<IFileLabelService> service = new();
        service.Setup(s => s.Get(It.IsAny<string>())).Returns<string>(path =>
            Path.GetFullPath(path) == Path.GetFullPath(redFile) ? new FileLabel(LabelColour.Red, ["invoice"]) : FileLabel.None);
        return service.Object;
    }

    private LabelFilterDialog Dialog(IFileLabelService labels) =>
        new(labels, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

    [Timeout(60_000)]
    [TestMethod]
    public async Task Apply_FiltersThePanelToTheColour()
    {
        var model = MarkableTestModel.Create(_root);
        var dialog = Dialog(Labels(Path.Combine(_root, "a.txt")));
        dialog.Load(model);
        await ShowAsync(dialog);
        dialog.ChooseColour("red");

        Assert.IsTrue(dialog.Apply());

        CollectionAssert.AreEqual(new[] { "..", "photos", "a.txt" }, model.FilteredEntries.Select(e => e.DisplayName).ToList());
        Assert.AreEqual("label: red", model.EntryFilterDescription);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnyColourAndABlankTag_ClearTheFilter()
    {
        var model = MarkableTestModel.Create(_root);
        model.SetEntryFilter(new LabelFilter(LabelColour.Red, null).ToEntryFilter(Labels(Path.Combine(_root, "a.txt"))), "label: red");
        var dialog = Dialog(Labels(Path.Combine(_root, "a.txt")));
        dialog.Load(model);
        await ShowAsync(dialog);

        Assert.IsTrue(dialog.Apply());

        Assert.AreEqual(5, model.FilteredEntries.Count);
        Assert.IsNull(model.EntryFilterDescription);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATagWithAComma_IsRefused()
    {
        var model = MarkableTestModel.Create(_root);
        var dialog = Dialog(Labels(Path.Combine(_root, "a.txt")));
        dialog.Load(model);
        await ShowAsync(dialog);
        dialog.Tag = "invoice, 2026";

        Assert.IsFalse(dialog.Apply());

        Assert.AreEqual("One tag at a time.", dialog.Message);
        Assert.IsNull(model.EntryFilterDescription);
    }
}

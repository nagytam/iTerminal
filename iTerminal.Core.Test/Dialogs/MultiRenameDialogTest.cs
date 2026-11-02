using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Factories;
using iTerminal.Core.Rename;
using iTerminal.Core.Services;
using iTerminal.Core.Test.Helper;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>Multi-rename (Edit menu): the pattern, the live preview and the count line.</summary>
[TestClass]
public class MultiRenameDialogTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "MultiRenameDialogTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
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

    internal static MultiRenameDialog NewDialog(TaskManager taskManager, IContentModelService service) =>
        new(taskManager, service, new ProgressWindowFactory(TestTheme()), Mock.Of<IErrorPromptService>(), Mock.Of<IConflictPromptService>(),
            new FileSystemResolver(new FileSystemService(), null!, null!, null!, service), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

    internal static Mock<IContentModelService> ServiceFor(IContentModel model)
    {
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetLastContentModel()).Returns(model);
        service.Setup(s => s.GetFocusedModel()).Returns(model);
        return service;
    }

    private static TextBox Box(MultiRenameDialog dialog, string label) =>
        dialog.Controls.AsSnapshot().OfType<TextBox>().Single(box => box.Label == label);

    internal static void Type(MultiRenameDialog dialog, string pattern, string find = "", string replace = "")
    {
        Box(dialog, "Pattern").Text = pattern;
        Box(dialog, "Find").Text = find;
        Box(dialog, "Replace").Text = replace;
        dialog.RefreshPlanIfChanged();
    }

    private static Label CountLine(MultiRenameDialog dialog) =>
        dialog.Controls.AsSnapshot().OfType<Label>().Single(label => label.Text.Contains("not renamed", StringComparison.Ordinal));

    private static ListBox<string> Preview(MultiRenameDialog dialog)
    {
        var list = dialog.Controls.AsSnapshot().OfType<ListBox<string>>().Single();
        list.UpdateModel();
        return list;
    }

    [TestMethod]
    public void TheDefaultPatternRenamesNothingAndSaysSo()
    {
        var model = MarkableTestModel.Create(_root);
        model.SetMark(model.Row("a.txt"), true);
        model.SetMark(model.Row("b.txt"), true);
        var dialog = NewDialog(new TaskManager(), ServiceFor(model).Object);

        dialog.Prepare(model, FocusedEntry.ActionableUrls(model));

        Assert.AreEqual(2, dialog.Plan.Count);
        Assert.AreEqual(0, dialog.OkCount);
        Assert.AreEqual("0 to rename, 2 not renamed", CountLine(dialog).Text);
    }

    [TestMethod]
    public void ATypedPatternShowsEachOldNameAndItsNewOne()
    {
        var model = MarkableTestModel.Create(_root);
        model.SetMark(model.Row("a.txt"), true);
        model.SetMark(model.Row("b.txt"), true);
        var dialog = NewDialog(new TaskManager(), ServiceFor(model).Object);
        dialog.Prepare(model, FocusedEntry.ActionableUrls(model));

        Type(dialog, "[C]_[N:2]");

        Assert.AreEqual(2, dialog.OkCount);
        Assert.AreEqual("2 to rename, 0 not renamed", CountLine(dialog).Text);
        var rows = Preview(dialog);
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("a.txt -> a_01.txt", rows.ListModel.ListItems[0].Value);
        Assert.AreEqual("b.txt -> b_02.txt", rows.ListModel.ListItems[1].Value);
    }

    [TestMethod]
    public void AStatusIsShownForARowThatWillNotBeRenamed()
    {
        var model = MarkableTestModel.Create(_root);
        model.AddFile("c.txt", "taken");
        model.SetMark(model.Row("a.txt"), true);
        var dialog = NewDialog(new TaskManager(), ServiceFor(model).Object);
        dialog.Prepare(model, FocusedEntry.ActionableUrls(model));

        Type(dialog, "[C]", find: "a", replace: "c");

        Assert.AreEqual(0, dialog.OkCount, "c.txt is already in the folder, so the row is Exists");
        Assert.AreEqual(RenameStatus.Exists, dialog.Plan.Single().Status);
        StringAssert.Contains(Preview(dialog).ListModel.ListItems[0].Value, "already exists");
    }

    [TestMethod]
    public void ThePreviewListCannotTakeFocus()
    {
        var dialog = NewDialog(new TaskManager(), Mock.Of<IContentModelService>());

        Assert.IsFalse(dialog.Controls.AsSnapshot().OfType<ListBox<string>>().Single().CanFocus,
            "a focusable list would take Enter ahead of the Rename command");
    }

    [TestMethod]
    public void ThePreviewDrawsAtMostTheLimitButTheCountCoversEveryRow()
    {
        const int count = MultiRenameDialog.PreviewLimit + 700;
        var model = MarkableTestModel.Create(_root);
        for (var index = 0; index < count; index++)
        {
            model.AddFile($"f{index:D5}.txt", "x");
        }

        foreach (var entry in model.FilteredEntries.Where(e => e.DisplayName.StartsWith('f')).ToList())
        {
            model.SetMark(entry, true);
        }

        var dialog = NewDialog(new TaskManager(), ServiceFor(model).Object);
        dialog.Prepare(model, FocusedEntry.ActionableUrls(model));
        Type(dialog, "[C]_x");

        Assert.AreEqual(count, dialog.Plan.Count);
        Assert.AreEqual(MultiRenameDialog.PreviewLimit, Preview(dialog).Count);
        StringAssert.Contains(CountLine(dialog).Text, $"{count} to rename");
    }

    [TestMethod]
    public void ChangingNothingKeepsThePlanAsItWas()
    {
        var model = MarkableTestModel.Create(_root);
        model.SetMark(model.Row("a.txt"), true);
        var dialog = NewDialog(new TaskManager(), ServiceFor(model).Object);
        dialog.Prepare(model, FocusedEntry.ActionableUrls(model));
        Type(dialog, "[C]_x");
        var before = dialog.Plan;

        dialog.RefreshPlanIfChanged();

        Assert.AreSame(before, dialog.Plan);
    }
}

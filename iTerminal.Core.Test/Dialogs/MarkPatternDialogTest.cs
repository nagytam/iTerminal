using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>Num+ / Num-: a pattern marks or unmarks the matching rows and closes; a pattern that matches nothing stays open and says so.</summary>
[TestClass]
public class MarkPatternDialogTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize() =>
        _root = Path.Combine(Path.GetTempPath(), "MarkPatternDialogTest_" + Guid.NewGuid().ToString("N")[..8]);

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

    private MarkPatternDialog NewDialog() => new(Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

    [Timeout(60_000)]
    [TestMethod]
    public async Task APatternThatMatches_MarksThemAndCloses()
    {
        var model = MarkableTestModel.Create(_root);
        var dialog = NewDialog();
        dialog.Target = model;
        var root = await ShowAsync(dialog);
        dialog.Pattern = "*.jpg";

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        CollectionAssert.AreEqual(new[] { "c.jpg" }, model.MarkedNames);
        Assert.IsFalse(root.Controls.Contains(dialog), "a pattern that marked something closes the dialog");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APatternThatMatchesNothing_StaysOpenAndSaysSo()
    {
        var model = MarkableTestModel.Create(_root);
        var dialog = NewDialog();
        dialog.Target = model;
        var root = await ShowAsync(dialog);
        dialog.Pattern = "*.zip";

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.IsTrue(root.Controls.Contains(dialog), "the user can fix the pattern");
        Assert.AreEqual(0, model.MarkedNames.Count());
        AssertScreenContains(Render(dialog), "No file matches *.zip.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Unmarking_WithNothingMarked_SaysNoMarkedFileMatches()
    {
        var model = MarkableTestModel.Create(_root);
        var dialog = NewDialog();
        dialog.Target = model;
        dialog.Marking = false;
        await ShowAsync(dialog);
        dialog.Pattern = "*.jpg";

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        AssertScreenContains(Render(dialog), "No marked file matches *.jpg.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithNoPanelToMarkIn_SaysSoAndStaysOpen()
    {
        var dialog = NewDialog();
        var root = await ShowAsync(dialog);
        dialog.Pattern = "*.jpg";

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.IsTrue(root.Controls.Contains(dialog));
        AssertScreenContains(Render(dialog), "There is no panel to mark in.");
    }
}

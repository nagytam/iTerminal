using iConsole.Controls;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Controls.MenuBar;
using iTerminal.Core.Dialogs.Test;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Controls.MenuBar;

/// <summary>The Edit menu's Multi-rename entry: there when its dialog is given, and named without a shortcut, as it has none.</summary>
[TestClass]
public class EditMenuMultiRenameTest
{
    private static IReadOnlyList<string> EntryTexts(EditMenuItem menu) =>
        [.. menu.Controls.AsSnapshot().OfType<MenuItem>().Select(item => item.Text)];

    [TestMethod]
    [Timeout(60_000)]
    public void TheEditMenuOffersMultiRenameWhenItsDialogIsGiven()
    {
        var service = Mock.Of<IContentModelService>();
        var dialog = MultiRenameDialogTest.NewDialog(new TaskManager(), service);

        var menu = new EditMenuItem(service, null!, null!, null!, null!, TestTheme(), multiRenameDialog: dialog);

        CollectionAssert.Contains(EntryTexts(menu).ToList(), "Multi-rename");
    }

    [TestMethod]
    [Timeout(60_000)]
    public void TheEntryFollowsRenameAndNamesNoShortcut()
    {
        var service = Mock.Of<IContentModelService>();
        var dialog = MultiRenameDialogTest.NewDialog(new TaskManager(), service);

        var texts = EntryTexts(new EditMenuItem(service, null!, null!, null!, null!, TestTheme(), multiRenameDialog: dialog)).ToList();

        Assert.AreEqual("Multi-rename", texts[texts.IndexOf("Rename\tF6") + 1]);
        Assert.IsFalse(texts.Single(text => text == "Multi-rename").Contains('\t'));
    }

    [TestMethod]
    [Timeout(60_000)]
    public void WithoutTheDialogTheEntryIsAbsent()
    {
        var service = Mock.Of<IContentModelService>();

        var menu = new EditMenuItem(service, null!, null!, null!, null!, TestTheme());

        CollectionAssert.DoesNotContain(EntryTexts(menu).ToList(), "Multi-rename");
    }
}

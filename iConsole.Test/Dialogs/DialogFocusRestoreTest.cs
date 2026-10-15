using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Test.Core;

namespace iConsole.Test.Dialogs;

/// <summary>
/// A dialog gives focus back to whatever had it. The stack it remembers that on used to be a single
/// static one for the whole process, so two screens — or two tests running side by side — popped each
/// other's entries: a dialog closing on one could hand focus to a control on the other.
/// </summary>
[TestClass]
public class DialogFocusRestoreTest
{
    private static (Control Root, Control Field) CreateScreen()
    {
        var theme = new TestThemeService();
        var root = new Control(theme) { ViewSize = new Size(80, 25) };
        var field = new Control(theme) { CanFocus = true, ViewSize = new Size(10, 1) };
        root.AddControl(field);
        field.SetFocus();
        return (root, field);
    }

    [TestMethod]
    public async Task ClosingADialogGivesFocusBackToTheControlThatHadIt()
    {
        var (root, field) = CreateScreen();
        var dialog = new Dialog("One", new TestThemeService());
        await field.ShowDialogAsync(dialog);
        field.ResetFocus();

        dialog.Close();

        Assert.IsTrue(field.IsFocused);
    }

    [TestMethod]
    public async Task TwoScreensKeepTheirOwnFocusWhateverOrderTheirDialogsCloseIn()
    {
        var (rootA, fieldA) = CreateScreen();
        var (rootB, fieldB) = CreateScreen();
        var dialogA = new Dialog("A", new TestThemeService());
        var dialogB = new Dialog("B", new TestThemeService());
        await fieldA.ShowDialogAsync(dialogA);
        await fieldB.ShowDialogAsync(dialogB);
        fieldA.ResetFocus();
        fieldB.ResetFocus();

        // Closed in the order they were opened: a shared stack hands A the entry B pushed last.
        dialogA.Close();

        Assert.IsTrue(fieldA.IsFocused, "screen A's field gets its focus back");
        Assert.IsFalse(fieldB.IsFocused, "and nothing on screen B is touched");

        dialogB.Close();

        Assert.IsTrue(fieldB.IsFocused);
        Assert.IsNotNull(rootA);
        Assert.IsNotNull(rootB);
    }
}

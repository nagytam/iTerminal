using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>The base of the small input forms: a typed character withdraws the error line, and the two buttons never share columns.</summary>
[TestClass]
public class FormDialogTest
{
    private sealed class Form : FormDialog
    {
        public Form() : base("form", TestTheme())
        {
        }

        public int ClearCount { get; private set; }

        protected override void ClearError() => ClearCount++;

        public void Lay(IControl primary, IControl cancel, Size size)
        {
            ViewSize = size;
            LayoutButtonRow(primary, cancel);
        }
    }

    [TestMethod]
    public async Task TypingACharacter_ClearsTheError()
    {
        var form = new Form();

        await form.HandleKeyEventAsync(Key(ConsoleKey.A, 'a'));

        Assert.AreEqual(1, form.ClearCount);
    }

    [TestMethod]
    public async Task BackspaceAndDelete_ClearTheError()
    {
        var form = new Form();

        await form.HandleKeyEventAsync(Key(ConsoleKey.Backspace));
        await form.HandleKeyEventAsync(Key(ConsoleKey.Delete));

        Assert.AreEqual(2, form.ClearCount);
    }

    [TestMethod]
    public async Task NavigationAndConfirmKeys_KeepTheError()
    {
        var form = new Form();

        await form.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await form.HandleKeyEventAsync(Key(ConsoleKey.Tab));
        await form.HandleKeyEventAsync(Key(ConsoleKey.Escape));
        await form.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));

        Assert.AreEqual(0, form.ClearCount, "the error stays until the user changes what it is about");
    }

    [TestMethod]
    public async Task AChord_IsNotTyping_SoItKeepsTheError()
    {
        var form = new Form();

        await form.HandleKeyEventAsync(new KeyEvent('a', ConsoleKey.A, false, false, true));

        Assert.AreEqual(0, form.ClearCount);
    }

    [TestMethod]
    public void ButtonRow_OnANarrowDialog_DoesNotShareColumnsWithThePrimaryButton()
    {
        var form = new Form();
        var primary = new Label(string.Empty, TestTheme());
        var cancel = new Label(string.Empty, TestTheme());

        form.Lay(primary, cancel, new Size(30, 7));

        Assert.IsTrue(primary.Position.X + primary.ViewSize.Width <= cancel.Position.X,
            $"primary {primary.Position.X}..{primary.Position.X + primary.ViewSize.Width} overlaps cancel from {cancel.Position.X}");
    }

    [TestMethod]
    public void ButtonRow_OnAWideDialog_RightAlignsCancel()
    {
        var form = new Form();
        var primary = new Label(string.Empty, TestTheme());
        var cancel = new Label(string.Empty, TestTheme());

        form.Lay(primary, cancel, new Size(60, 7));

        Assert.AreEqual(60 - 15 - 2, cancel.Position.X);
        Assert.AreEqual(3, primary.Position.X);
    }
}

using iConsole.Controls;
using iConsole.Data;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

[TestClass]
public class LinkLabelTest
{
    private readonly TestThemeService _theme = new();

    private LinkLabel Create(string text = "Docs")
    {
        var link = new LinkLabel(_theme) { Position = new Position(0, 0) };
        link.Text = text;
        return link;
    }

    [TestMethod]
    public async Task EnterAndSpaceFollowTheLinkWhenFocused()
    {
        var link = Create();
        var activated = 0;
        link.Activated += (_, _) => activated++;
        link.SetFocus();

        await link.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));
        await link.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));

        Assert.AreEqual(2, activated);
    }

    [TestMethod]
    public async Task KeysDoNothingWhenUnfocusedOrDisabled()
    {
        var link = Create();
        var activated = 0;
        link.Activated += (_, _) => activated++;

        await link.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));
        link.SetFocus();
        link.IsEnabled = false;
        await link.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));

        Assert.AreEqual(0, activated);
    }

    [TestMethod]
    public async Task ClickFollowsTheLinkAndTakesFocus()
    {
        var link = Create();
        var activated = 0;
        link.Activated += (_, _) => activated++;

        await link.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 0), LeftClicked = true });

        Assert.AreEqual(1, activated);
        Assert.IsTrue(link.IsFocused);
    }

    [TestMethod]
    public void FocusedLinkIsUnderlined_UnfocusedIsNot()
    {
        var root = new Control(_theme) { ViewSize = new Size(20, 1) };
        var link = Create();
        root.AddControl(link);

        Assert.IsFalse(ScreenBuffer.Render(root, 20, 1).CellOf("Docs").IsUnderline);

        link.SetFocus();

        Assert.IsTrue(ScreenBuffer.Render(root, 20, 1).CellOf("Docs").IsUnderline);
    }

    [TestMethod]
    public void KeyHints_ListActivationOnlyWhenEnabledWithText()
    {
        var link = Create();
        Assert.AreEqual("Enter/Space", link.GetKeyHints().Single().Key);

        link.IsEnabled = false;
        Assert.AreEqual(0, link.GetKeyHints().Count());

        link.IsEnabled = true;
        link.Text = string.Empty;
        Assert.AreEqual(0, link.GetKeyHints().Count());
    }
}

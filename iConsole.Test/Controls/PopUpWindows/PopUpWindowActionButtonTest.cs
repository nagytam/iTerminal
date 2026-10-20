using iConsole.Controls;
using iConsole.Controls.PopUpWindows;
using iConsole.Data;
using iConsole.Test.Core;

namespace iConsole.Test.Controls.PopUpWindows;

/// <summary>The [X] in a pop-up window's title bar can be clicked.</summary>
[TestClass]
public class PopUpWindowActionButtonTest
{
    private static MouseEvent Click(long x, long y) => new() { Position = new Position(x, y), LeftClicked = true };

    private static (Window Root, PopUpWindow Window) NewWindow()
    {
        TestThemeService theme = new();
        Window root = new(theme) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        PopUpWindow window = new("Info", theme) { Position = new Position(10, 5), ViewSize = new Size(30, 8) };
        root.AddControl(window);
        window.OnInitializeActionButtons();
        window.IsVisible = true;
        return (root, window);
    }

    [TestMethod]
    public async Task ClickOnTheCloseButton_HidesTheWindow()
    {
        (_, PopUpWindow window) = NewWindow();
        // The button is the three cells before the top-right corner.
        long x = window.AbsolutePosition.X + window.ViewSize.Width - 3;
        Assert.IsTrue(await window.HandleMouseEventAsync(Click(x, window.AbsolutePosition.Y)));
        Assert.IsFalse(window.IsVisible);
    }

    [TestMethod]
    public async Task ClickElsewhereOnTheTitleBar_DoesNotHideTheWindow()
    {
        (_, PopUpWindow window) = NewWindow();
        await window.HandleMouseEventAsync(Click(window.AbsolutePosition.X + 3, window.AbsolutePosition.Y));
        await window.HandleMouseEventAsync(new MouseEvent
        {
            Position = new Position(window.AbsolutePosition.X + window.ViewSize.Width - 3, window.AbsolutePosition.Y),
            RightClicked = true,
        });
        Assert.IsTrue(window.IsVisible);
    }

    [TestMethod]
    public async Task ClickBelowTheTitleBar_DoesNotPressTheCloseButton()
    {
        (_, PopUpWindow window) = NewWindow();
        await window.HandleMouseEventAsync(Click(window.AbsolutePosition.X + window.ViewSize.Width - 3, window.AbsolutePosition.Y + 1));
        Assert.IsTrue(window.IsVisible);
    }
}

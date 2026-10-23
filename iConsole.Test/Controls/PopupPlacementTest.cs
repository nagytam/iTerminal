using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Where a pop-up opens relative to its owner, how it stays on screen, and how it closes.</summary>
[TestClass]
public class PopupPlacementTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static KeyEvent Key(ConsoleKey key, bool control = false) => new('\0', key, false, false, control);

    private static (Window Root, CheckBox Owner, Popup Popup) Rig(uint ownerX = 20, uint ownerY = 10, uint rootWidth = 60, uint rootHeight = 20)
    {
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(rootWidth, rootHeight) };
        CheckBox owner = new(Theme()) { Position = new Position(ownerX, ownerY), ViewSize = new Size(10, 1) };
        root.AddControl(owner);
        Popup popup = new(Theme()) { ViewSize = new Size(12, 4) };
        return (root, owner, popup);
    }

    [TestMethod]
    public async Task Above_OpensTheHeightOfThePopupAboveTheOwner()
    {
        (_, CheckBox owner, Popup popup) = Rig();
        popup.Placement = PopupPlacement.Above;
        await popup.ShowAsync(owner);
        Assert.AreEqual((20L, 6L), (popup.Position.X, popup.Position.Y));
    }

    [TestMethod]
    public async Task Right_OpensBesideTheOwner_AndLeftEndsWhereItBegins()
    {
        (_, CheckBox owner, Popup right) = Rig();
        right.Placement = PopupPlacement.Right;
        await right.ShowAsync(owner);
        Assert.AreEqual((30L, 10L), (right.Position.X, right.Position.Y));

        (_, CheckBox owner2, Popup left) = Rig();
        left.Placement = PopupPlacement.Left;
        await left.ShowAsync(owner2);
        Assert.AreEqual((8L, 10L), (left.Position.X, left.Position.Y));
    }

    [TestMethod]
    public async Task Manual_OpensAtTheAnchor_AndWithoutOneStaysWhereItWas()
    {
        (_, CheckBox owner, Popup anchored) = Rig();
        anchored.Placement = PopupPlacement.Manual;
        anchored.Anchor = new Position(33, 7);
        await anchored.ShowAsync(owner);
        Assert.AreEqual((33L, 7L), (anchored.Position.X, anchored.Position.Y));

        (_, CheckBox owner2, Popup unanchored) = Rig();
        unanchored.Placement = PopupPlacement.Manual;
        unanchored.Position = new Position(4, 2);
        await unanchored.ShowAsync(owner2);
        Assert.AreEqual((4L, 2L), (unanchored.Position.X, unanchored.Position.Y));
    }

    [TestMethod]
    public async Task APopupNearAnEdge_IsMovedBackOntoTheScreen()
    {
        (_, CheckBox owner, Popup popup) = Rig(ownerX: 55, ownerY: 18);
        await popup.ShowAsync(owner);
        Assert.IsTrue(popup.Position.X + popup.ViewSize.Width <= 60, $"x {popup.Position.X}");
        Assert.IsTrue(popup.Position.Y + popup.ViewSize.Height <= 20, $"y {popup.Position.Y}");

        (_, CheckBox top, Popup above) = Rig(ownerX: 0, ownerY: 1);
        above.Placement = PopupPlacement.Above;
        await above.ShowAsync(top);
        Assert.AreEqual(0L, above.Position.Y);
        Assert.AreEqual(0L, above.Position.X);
    }

    [TestMethod]
    public async Task APopupBiggerThanTheScreen_IsPutAtTheCorner()
    {
        (_, CheckBox owner, Popup popup) = Rig(rootWidth: 8, rootHeight: 3, ownerX: 2, ownerY: 1);
        await popup.ShowAsync(owner);
        Assert.AreEqual((0L, 0L), (popup.Position.X, popup.Position.Y));
    }

    [TestMethod]
    public async Task ShowingAnOpenPopupAgain_DoesNotAttachItTwice()
    {
        (Window root, CheckBox owner, Popup popup) = Rig();
        await popup.ShowAsync(owner);
        await popup.ShowAsync(owner);
        Assert.AreEqual(1, root.Controls.AsSnapshot().Count(c => ReferenceEquals(c, popup)));
    }

    [TestMethod]
    public async Task SetContent_ReplacesThePreviousContent_AndSizesItToTheInterior()
    {
        (_, CheckBox owner, Popup popup) = Rig();
        Label first = new("one", Theme());
        Label second = new("two", Theme());
        popup.SetContent(first);
        popup.SetContent(second);
        Assert.IsFalse(popup.Controls.Contains(first));
        Assert.IsTrue(popup.Controls.Contains(second));
        await popup.ShowAsync(owner);
        Assert.AreEqual((1L, 1L), (second.Position.X, second.Position.Y));
        Assert.AreEqual((10L, 2L), (second.ViewSize.Width, second.ViewSize.Height));
        Assert.AreEqual(new Position(1, 1), popup.ContentPosition);
        popup.HasBorder = false;
        Assert.AreEqual(new Position(0, 0), popup.ContentPosition);
        Assert.AreEqual(12L, popup.ContentSize.Width);
    }

    [TestMethod]
    public async Task Escape_ClosesAnOpenPopup_UnlessItIsToldNotTo()
    {
        (_, CheckBox owner, Popup closing) = Rig();
        bool closed = false;
        closing.Closed += (_, _) => closed = true;
        await closing.ShowAsync(owner);
        Assert.IsTrue(await closing.HandleKeyEventAsync(Key(ConsoleKey.Escape)));
        Assert.IsFalse(closing.IsOpen);
        Assert.IsTrue(closed);

        (_, CheckBox owner2, Popup staying) = Rig();
        staying.CloseOnEscape = false;
        await staying.ShowAsync(owner2);
        await staying.HandleKeyEventAsync(Key(ConsoleKey.Escape));
        Assert.IsTrue(staying.IsOpen);
        await staying.HandleKeyEventAsync(Key(ConsoleKey.Escape, control: true));
        Assert.IsTrue(staying.IsOpen);
    }

    [TestMethod]
    public async Task AClickOutside_ClosesIt_UnlessItIsToldNotTo_AndAClickInsideDoesNot()
    {
        (_, CheckBox owner, Popup closing) = Rig();
        await closing.ShowAsync(owner);
        Assert.IsTrue(await closing.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 1), LeftClicked = true }));
        Assert.IsFalse(closing.IsOpen);

        (_, CheckBox owner2, Popup staying) = Rig();
        staying.CloseOnOutsideClick = false;
        await staying.ShowAsync(owner2);
        await staying.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 1), LeftClicked = true });
        Assert.IsTrue(staying.IsOpen);

        (_, CheckBox owner3, Popup inside) = Rig();
        await inside.ShowAsync(owner3);
        await inside.HandleMouseEventAsync(new MouseEvent { Position = new Position(inside.Position.X + 2, inside.Position.Y + 1), LeftClicked = true });
        Assert.IsTrue(inside.IsOpen);
    }

    [TestMethod]
    public async Task ANonModalPopup_LeavesTheFocusWithItsOwner()
    {
        (_, CheckBox owner, Popup popup) = Rig();
        owner.SetFocus();
        popup.Modal = false;
        await popup.ShowAsync(owner);
        Assert.IsTrue(owner.IsFocused);
        Assert.IsTrue(popup.IsOpen);
        popup.Close();
        Assert.IsTrue(owner.IsFocused);
    }

    [TestMethod]
    public async Task AModalPopup_GivesTheFocusBackWhenItCloses()
    {
        (_, CheckBox owner, Popup popup) = Rig();
        owner.SetFocus();
        popup.SetContent(new CheckBox(Theme()) { Label = "x" });
        await popup.ShowAsync(owner);
        Assert.IsFalse(owner.IsFocused);
        popup.Close();
        Assert.IsTrue(owner.IsFocused);
    }

    [TestMethod]
    public async Task ClosingAPopupThatWasNeverOpened_DoesNotThrow()
    {
        (_, CheckBox owner, Popup popup) = Rig();
        popup.Close();
        await popup.ShowAsync(owner);
        popup.Close();
        popup.Close();
        Assert.IsFalse(popup.IsOpen);
    }

    [TestMethod]
    public async Task Draw_PaintsTheBorderAndTitle_AndSkipsWhenEmpty()
    {
        (_, CheckBox owner, Popup popup) = Rig();
        popup.Title = "Pick";
        await popup.ShowAsync(owner);
        MockBuffer buffer = new(new Size(60, 20));
        popup.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Contains("Pick"));
        popup.ViewSize = new Size(0, 0);
        popup.Draw(new MockBuffer(new Size(60, 20)));
    }
}

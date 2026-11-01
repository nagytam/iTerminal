using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Test.Services;

[TestClass]
public class PopoverServiceTest
{
    private readonly TestThemeService _theme = new();

    private (Control Root, Control Owner) Screen()
    {
        Control root = new(_theme) { ViewSize = new Size(60, 20) };
        Control owner = new(_theme) { Position = new Position(5, 5), ViewSize = new Size(10, 1) };
        root.AddControl(owner);
        return (root, owner);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShowAtPointOpensThePopoverAtThePoint()
    {
        (Control root, Control owner) = Screen();
        PopoverService service = new();
        Popup popup = new(_theme) { ViewSize = new Size(10, 3) };

        await service.ShowAtPointAsync(popup, owner, new Position(20, 8));

        Assert.IsTrue(service.IsOpen);
        Assert.AreSame(popup, service.Current);
        Assert.AreEqual(new Position(20, 8), popup.Position);
        Assert.IsTrue(root.Controls.Contains(popup));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpeningASecondPopoverClosesTheFirst()
    {
        (Control root, Control owner) = Screen();
        PopoverService service = new();
        Popup first = new(_theme) { ViewSize = new Size(10, 3) };
        Popup second = new(_theme) { ViewSize = new Size(10, 3) };

        await service.ShowAtPointAsync(first, owner, new Position(2, 2));
        await service.ShowAtPointAsync(second, owner, new Position(30, 10));

        Assert.IsFalse(first.IsOpen);
        Assert.IsFalse(root.Controls.Contains(first));
        Assert.IsTrue(second.IsOpen);
        Assert.AreSame(second, service.Current);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClosingThePopoverClearsCurrent()
    {
        (_, Control owner) = Screen();
        PopoverService service = new();
        Popup popup = new(_theme) { ViewSize = new Size(10, 3) };
        await service.ShowAtPointAsync(popup, owner, new Position(2, 2));

        popup.Close();

        Assert.IsFalse(service.IsOpen);
        Assert.IsNull(service.Current);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CloseCurrentClosesWhateverIsOpen()
    {
        (Control root, Control owner) = Screen();
        PopoverService service = new();
        Popup popup = new(_theme) { ViewSize = new Size(10, 3) };
        await service.ShowAtPointAsync(popup, owner, new Position(2, 2));

        service.CloseCurrent();

        Assert.IsFalse(popup.IsOpen);
        Assert.IsFalse(root.Controls.Contains(popup));
        Assert.IsNull(service.Current);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShowBesideFlipsAboveWhenThereIsNoRoomBelow()
    {
        Control root = new(_theme) { ViewSize = new Size(60, 20) };
        Control target = new(_theme) { Position = new Position(5, 18), ViewSize = new Size(10, 1) };
        root.AddControl(target);
        PopoverService service = new();
        Popup popup = new(_theme) { ViewSize = new Size(10, 5) };

        await service.ShowBesideAsync(popup, target, target, PopoverSide.Below);

        Assert.AreEqual(new Position(5, 13), popup.Position);
    }
}

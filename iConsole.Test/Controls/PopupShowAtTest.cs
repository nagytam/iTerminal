using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

[TestClass]
public class PopupShowAtTest
{
    private readonly TestThemeService _theme = new();

    [TestMethod]
    public async Task ShowAt_PlacesTheOverlayAtTheAnchor()
    {
        var root = new Control(_theme) { ViewSize = new Size(60, 20) };
        var owner = new Control(_theme) { ViewSize = new Size(5, 1) };
        root.AddControl(owner);
        var popup = new Popup(_theme) { ViewSize = new Size(10, 3) };

        await popup.ShowAtAsync(owner, new Position(5, 7));

        Assert.IsTrue(popup.IsOpen);
        Assert.AreEqual(new Position(5, 7), popup.Position);
        Assert.IsTrue(root.Controls.Contains(popup));
    }

    [TestMethod]
    public async Task ShowAt_KeepsTheOverlayOnScreen()
    {
        var root = new Control(_theme) { ViewSize = new Size(60, 20) };
        var owner = new Control(_theme) { ViewSize = new Size(5, 1) };
        root.AddControl(owner);
        var popup = new Popup(_theme) { ViewSize = new Size(10, 3) };

        await popup.ShowAtAsync(owner, new Position(58, 19));

        Assert.AreEqual(new Position(50, 17), popup.Position);
    }

    [TestMethod]
    public async Task ShowAt_CloseRemovesTheOverlay()
    {
        var root = new Control(_theme) { ViewSize = new Size(60, 20) };
        var owner = new Control(_theme) { ViewSize = new Size(5, 1) };
        root.AddControl(owner);
        var popup = new Popup(_theme) { ViewSize = new Size(10, 3), Modal = false };

        await popup.ShowAtAsync(owner, new Position(1, 1));
        popup.Close();

        Assert.IsFalse(popup.IsOpen);
        Assert.IsFalse(root.Controls.Contains(popup));
    }
}

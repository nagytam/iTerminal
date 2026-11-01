using iConsole.Display;
using iConsole.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Graphics;

[TestClass]
public class SixelOverlayTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Show_ThenHide_ByTheSameOwner()
    {
        SixelOverlay overlay = new();
        object owner = new();

        overlay.Show(owner, 1, 2, 3, 4, "data", Color.Black);
        Assert.IsNotNull(overlay.Current);
        Assert.AreEqual(3, overlay.Current.Columns);

        overlay.Hide(owner);
        Assert.IsNull(overlay.Current);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Show_TheSamePictureAgain_KeepsThePlacement()
    {
        SixelOverlay overlay = new();
        object owner = new();
        string data = new('x', 10);

        overlay.Show(owner, 1, 2, 3, 4, data, Color.Black);
        SixelPlacement? first = overlay.Current;
        overlay.Show(owner, 1, 2, 3, 4, data, Color.Black);

        Assert.AreSame(first, overlay.Current);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Show_AMovedPicture_ReplacesThePlacement()
    {
        SixelOverlay overlay = new();
        object owner = new();
        string data = new('x', 10);

        overlay.Show(owner, 1, 2, 3, 4, data, Color.Black);
        SixelPlacement? first = overlay.Current;
        overlay.Show(owner, 5, 2, 3, 4, data, Color.Black);

        Assert.AreNotSame(first, overlay.Current);
        Assert.IsFalse(overlay.Current!.IsSameAs(first));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Hide_ByAnotherOwner_LeavesThePicture()
    {
        SixelOverlay overlay = new();
        overlay.Show(new object(), 0, 0, 1, 1, "data", Color.Black);

        overlay.Hide(new object());

        Assert.IsNotNull(overlay.Current);
    }
}

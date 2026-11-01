#pragma warning disable MSTEST0032 // These tests pin a constant's value on purpose.
using iConsole.Display;

namespace iConsole.Test.Display;

[TestClass]
public class BorderTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void BorderTopLeft()
    {
        Assert.AreEqual("╔", Border.TopLeft);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderTopRight()
    {
        Assert.AreEqual("╗", Border.TopRight);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderBottomLeft()
    {
        Assert.AreEqual("╚", Border.BottomLeft);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderBottomRight()
    {
        Assert.AreEqual("╝", Border.BottomRight);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderMiddleLeft()
    {
        Assert.AreEqual("╠", Border.MiddleLeft);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderMiddleRight()
    {
        Assert.AreEqual("╣", Border.MiddleRight);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderHorizontal()
    {
        Assert.AreEqual("═", Border.Horizontal);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderHorizontalSingle()
    {
        Assert.AreEqual("⎯", Border.HorizontalSingle);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderVertical()
    {
        Assert.AreEqual("║", Border.Vertical);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderVerticalSingle()
    {
        Assert.AreEqual("│", Border.VerticalSingle);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderStart()
    {
        Assert.AreEqual("[", Border.Start);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderEnd()
    {
        Assert.AreEqual("]", Border.End);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderOpenToLeft()
    {
        Assert.AreEqual("<", Border.OpenToLeft);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderOpenToRight()
    {
        Assert.AreEqual(">", Border.OpenToRight);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderArrowUp()
    {
        Assert.AreEqual('↑', Border.ArrowUp);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderArrowDown()
    {
        Assert.AreEqual('↓', Border.ArrowDown);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderArrowLeft()
    {
        Assert.AreEqual('←', Border.ArrowLeft);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderArrowRight()
    {
        Assert.AreEqual('→', Border.ArrowRight);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderScrollPosition()
    {
        Assert.AreEqual("⬤", Border.ScrollPosition);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BorderConstantsNotNull()
    {
        Assert.IsNotNull(Border.TopLeft);
        Assert.IsNotNull(Border.TopRight);
        Assert.IsNotNull(Border.BottomLeft);
        Assert.IsNotNull(Border.BottomRight);
        Assert.IsNotNull(Border.Horizontal);
        Assert.IsNotNull(Border.Vertical);
        Assert.IsNotNull(Border.Start);
        Assert.IsNotNull(Border.End);
    }
}

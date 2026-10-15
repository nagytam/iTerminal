using iConsole.Display;

namespace iConsole.Test.Display;

[TestClass]
public class BorderTest
{
    [TestMethod]
    public void BorderTopLeft()
    {
        Assert.AreEqual("╔", Border.TopLeft);
    }

    [TestMethod]
    public void BorderTopRight()
    {
        Assert.AreEqual("╗", Border.TopRight);
    }

    [TestMethod]
    public void BorderBottomLeft()
    {
        Assert.AreEqual("╚", Border.BottomLeft);
    }

    [TestMethod]
    public void BorderBottomRight()
    {
        Assert.AreEqual("╝", Border.BottomRight);
    }

    [TestMethod]
    public void BorderMiddleLeft()
    {
        Assert.AreEqual("╠", Border.MiddleLeft);
    }

    [TestMethod]
    public void BorderMiddleRight()
    {
        Assert.AreEqual("╣", Border.MiddleRight);
    }

    [TestMethod]
    public void BorderHorizontal()
    {
        Assert.AreEqual("═", Border.Horizontal);
    }

    [TestMethod]
    public void BorderHorizontalSingle()
    {
        Assert.AreEqual("⎯", Border.HorizontalSingle);
    }

    [TestMethod]
    public void BorderVertical()
    {
        Assert.AreEqual("║", Border.Vertical);
    }

    [TestMethod]
    public void BorderVerticalSingle()
    {
        Assert.AreEqual("│", Border.VerticalSingle);
    }

    [TestMethod]
    public void BorderStart()
    {
        Assert.AreEqual("[", Border.Start);
    }

    [TestMethod]
    public void BorderEnd()
    {
        Assert.AreEqual("]", Border.End);
    }

    [TestMethod]
    public void BorderOpenToLeft()
    {
        Assert.AreEqual("<", Border.OpenToLeft);
    }

    [TestMethod]
    public void BorderOpenToRight()
    {
        Assert.AreEqual(">", Border.OpenToRight);
    }

    [TestMethod]
    public void BorderArrowUp()
    {
        Assert.AreEqual('↑', Border.ArrowUp);
    }

    [TestMethod]
    public void BorderArrowDown()
    {
        Assert.AreEqual('↓', Border.ArrowDown);
    }

    [TestMethod]
    public void BorderArrowLeft()
    {
        Assert.AreEqual('←', Border.ArrowLeft);
    }

    [TestMethod]
    public void BorderArrowRight()
    {
        Assert.AreEqual('→', Border.ArrowRight);
    }

    [TestMethod]
    public void BorderScrollPosition()
    {
        Assert.AreEqual("⬤", Border.ScrollPosition);
    }

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

using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class SizeTest
{
    [TestMethod]
    public void SizeConstructorLong()
    {
        var size = new Size(80, 24);
        Assert.AreEqual(80, size.Width);
        Assert.AreEqual(24, size.Height);
    }

    [TestMethod]
    public void SizeConstructorInt()
    {
        var size = new Size(80, 24);
        Assert.AreEqual(80, size.Width);
        Assert.AreEqual(24, size.Height);
    }

    [TestMethod]
    public void SizeZero()
    {
        var size = new Size(0, 0);
        Assert.AreEqual(0, size.Width);
        Assert.AreEqual(0, size.Height);
        Assert.IsTrue(size.IsEmpty);
    }

    [TestMethod]
    public void SizeIsEmptyWithZeroWidth()
    {
        var size = new Size(0, 24);
        Assert.IsTrue(size.IsEmpty);
    }

    [TestMethod]
    public void SizeIsEmptyWithZeroHeight()
    {
        var size = new Size(80, 0);
        Assert.IsTrue(size.IsEmpty);
    }

    [TestMethod]
    public void SizeIsNotEmpty()
    {
        var size = new Size(80, 24);
        Assert.IsFalse(size.IsEmpty);
    }

    [TestMethod]
    public void SizeSetWidth()
    {
        var size = new Size(0, 0);
        size.Width = 100;
        Assert.AreEqual(100, size.Width);
    }

    [TestMethod]
    public void SizeSetHeight()
    {
        var size = new Size(0, 0);
        size.Height = 50;
        Assert.AreEqual(50, size.Height);
    }

    [TestMethod]
    public void SizeIntersect()
    {
        var size1 = new Size(100, 50);
        var size2 = new Size(80, 60);
        size1.Intersect(size2);
        Assert.AreEqual(80, size1.Width);
        Assert.AreEqual(50, size1.Height);
    }

    [TestMethod]
    public void SizeMultiplicationOperator()
    {
        var size = new Size(100, 50);
        var result = size * 2.0;
        Assert.AreEqual(200, result.Width);
        Assert.AreEqual(100, result.Height);
    }

    [TestMethod]
    public void SizeMultiplicationOperatorFraction()
    {
        var size = new Size(100, 50);
        var result = size * 0.5;
        Assert.AreEqual(50, result.Width);
        Assert.AreEqual(25, result.Height);
    }

    [TestMethod]
    public void SizeReset()
    {
        var size = new Size(100, 50);
        size.Reset();
        Assert.AreEqual(0, size.Width);
        Assert.AreEqual(0, size.Height);
    }

    [TestMethod]
    public void SizePropertyChanged()
    {
        var size = new Size(0, 0);
        bool widthChanged = false;
        bool heightChanged = false;

        size.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Size.Width)) widthChanged = true;
            if (e.PropertyName == nameof(Size.Height)) heightChanged = true;
        };

        size.Width = 80;
        Assert.IsTrue(widthChanged);

        size.Height = 24;
        Assert.IsTrue(heightChanged);
    }

    [TestMethod]
    public void SizeEquality()
    {
        var size1 = new Size(80, 24);
        var size2 = new Size(80, 24);
        var size3 = new Size(80, 25);

        Assert.AreEqual(size1, size2);
        Assert.AreNotEqual(size1, size3);
    }

    [TestMethod]
    public void SizeToString()
    {
        var size = new Size(80, 24);
        var str = size.ToString();
        Assert.IsTrue(str.Contains("80") && str.Contains("24"));
    }

    [TestMethod]
    public void SizeLargeValues()
    {
        var size = new Size(1000000, 1000000);
        Assert.AreEqual(1000000, size.Width);
        Assert.AreEqual(1000000, size.Height);
    }
}

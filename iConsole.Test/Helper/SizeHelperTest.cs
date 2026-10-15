using iConsole.Data;
using iConsole.Helper;

namespace iConsole.Test.Helper;

[TestClass]
public class SizeHelperTest
{
    [TestMethod]
    public void SizeHelperSize1x1()
    {
        Assert.IsNotNull(SizeHelper.Size1x1);
        Assert.AreEqual(1, SizeHelper.Size1x1.Width);
        Assert.AreEqual(1, SizeHelper.Size1x1.Height);
    }

    [TestMethod]
    public void SizeHelperSize1x1NotEmpty()
    {
        Assert.IsFalse(SizeHelper.Size1x1.IsEmpty);
    }

    [TestMethod]
    public void SizeHelperSize1x1Singleton()
    {
        var size1 = SizeHelper.Size1x1;
        var size2 = SizeHelper.Size1x1;
        Assert.AreEqual(size1, size2);
    }
}

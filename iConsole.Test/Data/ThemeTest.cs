using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class ThemeTest
{
    [TestMethod]
    public void Theme_Default_HasValue()
    {
        var theme = Theme.Default;
        Assert.AreEqual(0, (int)theme);
    }

    [TestMethod]
    public void Theme_Dark_HasValue()
    {
        var theme = Theme.Dark;
        Assert.AreEqual(1, (int)theme);
    }

    [TestMethod]
    public void Theme_CanCompare()
    {
        var theme1 = Theme.Default;
        var theme2 = Theme.Default;
        var theme3 = Theme.Dark;

        Assert.AreEqual(theme1, theme2);
        Assert.AreNotEqual(theme1, theme3);
    }

    [TestMethod]
    public void Theme_CanConvertToString()
    {
        var theme = Theme.Default;
        var str = theme.ToString();
        Assert.IsFalse(string.IsNullOrEmpty(str));
    }
}

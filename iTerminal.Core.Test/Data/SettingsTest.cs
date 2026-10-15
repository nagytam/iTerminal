using iTerminal.Core.Data;

namespace iTerminal.Core.Test.Data;

[TestClass]
public class SettingsTest
{
    [TestMethod]
    public void Settings_IsQuotesIndicatorEnabled_HasCorrectValue()
    {
        Assert.AreEqual("IsQuotesIndicatorEnabled", Settings.IsQuotesIndicatorEnabled);
    }

    [TestMethod]
    public void Settings_IsQuotesIndicatorEnabled_IsConstant()
    {
        var value1 = Settings.IsQuotesIndicatorEnabled;
        var value2 = Settings.IsQuotesIndicatorEnabled;
        Assert.AreSame(value1, value2);
    }

    [TestMethod]
    public void Settings_IsQuotesIndicatorEnabled_IsNotEmpty()
    {
        Assert.IsFalse(string.IsNullOrEmpty(Settings.IsQuotesIndicatorEnabled));
    }

    [TestMethod]
    public void Settings_IsQuotesIndicatorEnabled_MatchesNameof()
    {
        var expected = nameof(Settings.IsQuotesIndicatorEnabled);
        Assert.AreEqual(expected, Settings.IsQuotesIndicatorEnabled);
    }
}

#pragma warning disable MSTEST0032 // These tests pin a constant's value on purpose.
using iTerminal.Core.Data;

namespace iTerminal.Core.Test.Data;

[TestClass]
public class SettingsTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Settings_IsQuotesIndicatorEnabled_HasCorrectValue()
    {
        Assert.AreEqual("IsQuotesIndicatorEnabled", Settings.IsQuotesIndicatorEnabled);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Settings_IsQuotesIndicatorEnabled_IsConstant()
    {
        var value1 = Settings.IsQuotesIndicatorEnabled;
        var value2 = Settings.IsQuotesIndicatorEnabled;
        Assert.AreSame(value1, value2);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Settings_IsQuotesIndicatorEnabled_IsNotEmpty()
    {
        Assert.IsFalse(string.IsNullOrEmpty(Settings.IsQuotesIndicatorEnabled));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Settings_IsQuotesIndicatorEnabled_MatchesNameof()
    {
        var expected = nameof(Settings.IsQuotesIndicatorEnabled);
        Assert.AreEqual(Settings.IsQuotesIndicatorEnabled, expected);
    }
}

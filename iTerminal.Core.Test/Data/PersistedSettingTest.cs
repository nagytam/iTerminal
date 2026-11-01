using Microsoft.VisualStudio.TestTools.UnitTesting;
using iTerminal.Core.Data;

namespace iTerminal.Core.Data.Test;

/// <summary>A stored setting is a key and its text value; neither is ever null, so a read cannot fail on a missing one.</summary>
[TestClass]
public class PersistedSettingTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void A_NewSetting_HasEmptyKeyAndValue_NotNull()
    {
        var setting = new PersistedSetting();

        Assert.AreEqual(string.Empty, setting.Key);
        Assert.AreEqual(string.Empty, setting.Value);
    }
}

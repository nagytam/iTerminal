using iTerminal.Core.Helper;

namespace iTerminal.Core.Test.Helper;

[TestClass]
public class DriveTypeTextTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void InEnglish_EachTypeKeepsTheNameTheListAlwaysPrinted()
    {
        foreach (DriveType type in new[] { DriveType.Fixed, DriveType.Removable, DriveType.CDRom, DriveType.Network, DriveType.Ram, DriveType.NoRootDirectory })
        {
            Assert.AreEqual(type.ToString(), DriveTypeText.Of(type));
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnUnknownTypeIsSaidSo_AndNoTypeIsEmpty()
    {
        Assert.AreEqual("Unknown", DriveTypeText.Of(DriveType.Unknown));
        Assert.AreEqual(string.Empty, DriveTypeText.Of(null));
    }
}

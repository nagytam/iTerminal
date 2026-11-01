using iTerminal.Core.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Model.Test;

/// <summary>The disk list starts "not read yet" — which the disk manager relies on to say "Reading…" rather than "No drives".</summary>
[TestClass]
public class DriveInfoModelTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void A_NewModel_HasNoDrives_AndIsNotYetRead()
    {
        var model = new DriveInfoModel();

        Assert.AreEqual(0, model.Drives.Count);
        Assert.IsFalse(model.IsLoaded);
    }
}

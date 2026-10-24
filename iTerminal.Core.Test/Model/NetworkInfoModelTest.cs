using iTerminal.Core.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Model.Test;

/// <summary>The network list starts "not read yet", as the disk list does, so the manager says "Reading…" until the first pass.</summary>
[TestClass]
public class NetworkInfoModelTest
{
    [TestMethod]
    public void A_NewModel_HasNoNetworks_AndIsNotYetRead()
    {
        var model = new NetworkInfoModel();

        Assert.AreEqual(0, model.Networks.Count);
        Assert.IsFalse(model.IsLoaded);
    }
}

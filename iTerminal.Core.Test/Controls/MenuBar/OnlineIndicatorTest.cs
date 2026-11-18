using Microsoft.VisualStudio.TestTools.UnitTesting;
using iTerminal.Core.Controls.MenuBar;
using iTerminal.Core.Services;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Controls.MenuBar.Test;

[TestClass]
public class OnlineIndicatorTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void BeforeTheFirstCheckItSaysChecking()
    {
        var indicator = new OnlineIndicator(new InternetConnectivityService(), TestTheme());

        Assert.AreEqual("Checking".PadRight(10), indicator.Describe(10));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WhenTheInternetIsReachableItSaysOnline()
    {
        var service = new InternetConnectivityService();
        service.Report(true);
        var indicator = new OnlineIndicator(service, TestTheme());

        Assert.AreEqual("Online".PadRight(10), indicator.Describe(10));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WhenTheInternetIsNotReachableItSaysOffline()
    {
        var service = new InternetConnectivityService();
        service.Report(false);
        var indicator = new OnlineIndicator(service, TestTheme());

        Assert.AreEqual("Offline".PadRight(10), indicator.Describe(10));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ANarrowSlotCutsTheWordRatherThanOverflowingIt()
    {
        var service = new InternetConnectivityService();
        service.Report(false);
        var indicator = new OnlineIndicator(service, TestTheme());

        Assert.AreEqual(3, indicator.Describe(3).Length);
    }
}

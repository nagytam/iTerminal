using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Controls;
using iConsole.Data;
using iTerminal.Core.Dialogs;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class SettingsGlobalTabPageTest
{
    private static List<string> Rows(int width)
    {
        var page = new SettingsGlobalTabPage(TestTheme()) { ViewSize = new Size(width, 12) };
        page.OnInitializeLayout();
        return [.. page.Controls.AsSnapshot().OfType<Label>().OrderBy(l => l.Position.Y).Select(l => l.Text).Where(t => t.Length > 0)];
    }

    [TestMethod]
    public void SaysWhereTheAppWideSettingsLive()
    {
        CollectionAssert.AreEqual(SettingsGlobalTabPage.Lines, Rows(120), "a wide page shows each line on one row");
    }

    /// <summary>On an 80-column screen the page is ~43 wide; lines used to be cut at its edge.</summary>
    [TestMethod]
    public void ANarrowPageWrapsInsteadOfCutting()
    {
        var rows = Rows(43);

        Assert.IsTrue(rows.All(r => r.Length <= 43), string.Join("\n", rows));
        var words = string.Join(" ", rows).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var expected = string.Join(" ", SettingsGlobalTabPage.Lines).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        CollectionAssert.AreEqual(expected, words, "every word is still there");
    }
    [TestMethod]
    public async Task TheSixelFlag_IsOnByDefault_AndFollowsTheSetting()
    {
        var settings = iTerminal.Core.Services.NullSettingsService.Instance;
        var page = new SettingsGlobalTabPage(TestTheme(), settings);
        await page.OnInitializeAsync();
        Assert.IsTrue(page.IsSixelEnabled);

        var stored = new Moq.Mock<iTerminal.Core.Services.ISettingsService>();
        stored.Setup(s => s.GetAsBool(iTerminal.Core.Data.Settings.SixelEnabled, true)).Returns(false);
        var off = new SettingsGlobalTabPage(TestTheme(), stored.Object);
        await off.OnInitializeAsync();
        Assert.IsFalse(off.IsSixelEnabled);
    }

    [TestMethod]
    public async Task ChangingTheSixelFlag_IsSaved()
    {
        var stored = new Moq.Mock<iTerminal.Core.Services.ISettingsService>();
        stored.Setup(s => s.GetAsBool(iTerminal.Core.Data.Settings.SixelEnabled, true)).Returns(true);
        var page = new SettingsGlobalTabPage(TestTheme(), stored.Object);
        await page.OnInitializeAsync();

        page.IsSixelEnabled = false;

        stored.Verify(s => s.Set(iTerminal.Core.Data.Settings.SixelEnabled, bool.FalseString), Moq.Times.Once());
    }

    [TestMethod]
    public void NameAndHotKey()
    {
        var page = new SettingsGlobalTabPage(TestTheme());

        Assert.AreEqual(ConsoleKey.D1, page.HotConsoleKey);
    }
}

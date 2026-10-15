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
    public void NameAndHotKey()
    {
        var page = new SettingsGlobalTabPage(TestTheme());

        Assert.AreEqual(ConsoleKey.D1, page.HotConsoleKey);
    }
}

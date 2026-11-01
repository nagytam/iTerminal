using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Data;
using iTerminal.Core.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>Page 3 of the settings dialog: the menu bar's quote and weather indicators.</summary>
[TestClass]
public class SettingsQuotesTabPageTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void BothIndicatorsAreOnByDefault()
    {
        var page = new SettingsQuotesTabPage(new SettingsService(), TestTheme());

        Assert.IsTrue(page.IsQuotesIndicatorEnabled);
        Assert.IsTrue(page.IsWeatherIndicatorEnabled);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task StoredChoicesAreLoaded()
    {
        var settings = new SettingsService();
        settings.Set(Settings.IsQuotesIndicatorEnabled, bool.FalseString);
        settings.Set(Settings.IsWeatherIndicatorEnabled, bool.FalseString);
        var page = new SettingsQuotesTabPage(settings, TestTheme());

        await page.OnInitializeAsync();

        Assert.IsFalse(page.IsQuotesIndicatorEnabled);
        Assert.IsFalse(page.IsWeatherIndicatorEnabled);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ChangingAChoiceIsStoredAtOnce()
    {
        var settings = new SettingsService();
        var page = new SettingsQuotesTabPage(settings, TestTheme());

        page.IsWeatherIndicatorEnabled = false;

        Assert.AreEqual(bool.FalseString, settings.Get(Settings.IsWeatherIndicatorEnabled));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheCheckBoxesAreStackedOneRowEach()
    {
        var page = new SettingsQuotesTabPage(new SettingsService(), TestTheme()) { ViewSize = new Size(40, 5) };

        page.OnInitializeLayout();

        var rows = page.Controls.AsSnapshot().Select(c => (int)c.Position.Y).ToList();
        CollectionAssert.AreEqual(new[] { 0, 1 }, rows);
    }
}

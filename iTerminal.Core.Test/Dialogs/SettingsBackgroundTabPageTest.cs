using iConsole.Controls;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.BackgroundStart;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>Tab 7 of the settings dialog: when each background service starts.</summary>
[TestClass]
public class SettingsBackgroundTabPageTest
{
    private static SettingsBackgroundTabPage CreatePage(ISettingsService settingsService) =>
        new(settingsService, TestTheme());

    private static List<ComboBox<BackgroundStartMode>> ModeBoxes(SettingsBackgroundTabPage page) =>
        page.Controls.AsSnapshot().OfType<ComboBox<BackgroundStartMode>>().ToList();

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnInitialize_ShowsTheStoredMode()
    {
        var settings = new SettingsService();
        settings.Set(BackgroundStartService.SettingKey(BackgroundServices.FolderIndex), "Immediately");
        settings.Set(BackgroundStartService.SettingKey(BackgroundServices.MusicLibrary), "Delayed");
        var page = CreatePage(settings);

        await page.OnInitializeAsync();

        var boxes = ModeBoxes(page);
        Assert.AreEqual(BackgroundServices.All.Count, boxes.Count);
        Assert.AreEqual(BackgroundStartMode.Immediately, boxes[0].SelectedItem);
        Assert.AreEqual(BackgroundStartMode.Delayed, boxes[2].SelectedItem);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnInitialize_IsOnDemandWhenNothingIsStored()
    {
        var page = CreatePage(new SettingsService());

        await page.OnInitializeAsync();

        foreach (var box in ModeBoxes(page))
        {
            Assert.AreEqual(BackgroundStartMode.OnDemand, box.SelectedItem);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ChangingAModeStoresIt()
    {
        var settings = new SettingsService();
        var page = CreatePage(settings);
        await page.OnInitializeAsync();

        ModeBoxes(page)[0].SelectedIndex = (int)BackgroundStartMode.Delayed;

        Assert.AreEqual("Delayed", settings.Get(BackgroundStartService.SettingKey(BackgroundServices.FolderIndex)));
    }
}

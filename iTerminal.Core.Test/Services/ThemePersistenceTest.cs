using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Services;

/// <summary>The chosen theme outlives the session — it used to reset to the default at every start-up.</summary>
[TestClass]
public class ThemePersistenceTest
{
    [TestMethod]
    public async Task ApplyingAThemeInTheManagerSavesIt()
    {
        var settings = new SettingsService();
        var dialog = new ThemeManagerDialog(new ThemeService(), settings);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.AreEqual(nameof(Theme.Dark), settings.Get(Settings.Theme));
    }

    [TestMethod]
    public void TheSavedThemeIsRestored()
    {
        var settings = new SettingsService();
        ThemePersistence.Save(settings, Theme.Dark);
        var themeService = new ThemeService();

        Assert.IsTrue(ThemePersistence.Restore(themeService, settings));
        Assert.AreEqual(Theme.Dark, themeService.Theme);
    }

    [TestMethod]
    public void NoSavedThemeLeavesTheDefault()
    {
        var themeService = new ThemeService();

        Assert.IsFalse(ThemePersistence.Restore(themeService, new SettingsService()));
        Assert.AreEqual(Theme.Default, themeService.Theme);
    }

    [TestMethod]
    public void AnUnknownSavedThemeIsIgnored()
    {
        var settings = new SettingsService();
        settings.Set(Settings.Theme, "Neon");
        var themeService = new ThemeService();

        Assert.IsFalse(ThemePersistence.Restore(themeService, settings));
        Assert.AreEqual(Theme.Default, themeService.Theme);
    }

    [TestMethod]
    public void AServiceThatCannotSwitchIsLeftAlone()
    {
        var settings = new SettingsService();
        ThemePersistence.Save(settings, Theme.Dark);

        Assert.IsFalse(ThemePersistence.Restore(TestTheme(), settings));
    }

    [TestMethod]
    public void WithoutSettingsNothingHappens()
    {
        ThemePersistence.Save(null, Theme.Dark);

        Assert.IsFalse(ThemePersistence.Restore(new ThemeService(), null));
    }
}

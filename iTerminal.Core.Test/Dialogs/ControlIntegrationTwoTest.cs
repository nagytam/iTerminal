using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Dialogs.PasswordManager;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>Controls the app's dialogs put to use: the theme list's type-ahead, the music volume slider, the password strength meter.</summary>
[TestClass]
public class ControlIntegrationTwoTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task ThemeManager_TypingALetterJumpsToTheNextThemeStartingWithIt()
    {
        var themeService = new ThemeService();
        var dialog = new ThemeManagerDialog(themeService);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('d'));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.AreEqual(Theme.Dark, themeService.Theme);
    }

    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), $"iTerminal.ControlsTwo.{Guid.NewGuid():N}");

    [Timeout(60_000)]
    [TestMethod]
    public async Task PasswordManager_EditFormRatesThePasswordBeingTyped()
    {
        var manager = new iTerminal.Core.Services.PasswordManager(NewDirectory() + ".sqlite", NewDirectory());
        manager.Unlock();
        var dialog = new PasswordManagerDialog(manager, TestTheme());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('n'));
        for (var i = 0; i < 3; i++)
        {
            await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        }

        foreach (var c in "abc")
        {
            await dialog.HandleKeyEventAsync(Char(c));
        }

        AssertScreenContains(Render(dialog), "Weak");
        Assert.AreEqual(iConsole.Controls.PasswordStrength.Weak, dialog.StrengthForTest);

        foreach (var c in "Xk9!vQ2#mLp7")
        {
            await dialog.HandleKeyEventAsync(Char(c));
        }

        AssertScreenContains(Render(dialog), "Strong");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SettingsMusic_VolumeSliderShowsAndSetsThePlayersVolume()
    {
        var player = new Mock<IMusicPlayerService>();
        player.SetupProperty(p => p.Volume, 40);
        var page = new SettingsMusicTabPage(new SettingsService(), Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme(), player.Object);

        await page.OnInitializeAsync();
        Assert.AreEqual(40, page.Volume);

        page.Volume = 75;
        Assert.AreEqual(75, player.Object.Volume);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SettingsMusic_WithoutAPlayerTheSliderIsHarmless()
    {
        var page = new SettingsMusicTabPage(new SettingsService(), Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());

        await page.OnInitializeAsync();
        page.Volume = 10;

        Assert.AreEqual(10, page.Volume);
    }
}

using iConsole.Display;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class SettingsMusicTabPageTest
{
    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private static SettingsMusicTabPage CreatePage(ISettingsService settingsService) =>
        new(settingsService, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), CreateThemeService());

    private static string ExpectedSystemDefaultMusicFolder() =>
        OperatingSystem.IsLinux()
            ? "/home/tamas/nata/music"
            : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

    [TestMethod]
    public async Task OnInitialize_LoadsTheStoredMusicFolder()
    {
        var settings = new SettingsService();
        settings.Set(Settings.MusicFolder, @"D:\Media\Music");

        var page = CreatePage(settings);
        await page.OnInitializeAsync();

        Assert.AreEqual(@"D:\Media\Music", page.MusicFolder);
    }

    [TestMethod]
    public async Task EditingTheFolder_WritesTheSettingStraightAway()
    {
        var settings = new SettingsService();
        var page = CreatePage(settings);
        await page.OnInitializeAsync();

        page.MusicFolder = @"C:\Songs";

        Assert.AreEqual(@"C:\Songs", settings.Get(Settings.MusicFolder));
    }

    [TestMethod]
    public async Task ClearingTheFolder_StoresAnEmptyValueSoTheSystemDefaultTakesOver()
    {
        var settings = new SettingsService();
        settings.Set(Settings.MusicFolder, @"C:\Songs");
        var page = CreatePage(settings);
        await page.OnInitializeAsync();

        page.MusicFolder = string.Empty;

        Assert.AreEqual(string.Empty, settings.Get(Settings.MusicFolder));
        Assert.AreEqual(
            ExpectedSystemDefaultMusicFolder(),
            iTerminal.Core.Services.Music.MusicLibraryService.ResolveMusicFolder(settings));
    }

    [TestMethod]
    public async Task WithNoStoredFolder_TheBoxIsEmpty()
    {
        var page = CreatePage(new SettingsService());
        await page.OnInitializeAsync();

        Assert.AreEqual(string.Empty, page.MusicFolder);
    }
}

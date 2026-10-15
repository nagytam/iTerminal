using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Test.Services;

[TestClass]
public class ThemeServiceTest
{
    [TestMethod]
    public void ThemeServiceConstructor()
    {
        var service = new ThemeService();
        Assert.IsNotNull(service);
        Assert.IsNotNull(service.ColorSchemes);
        Assert.IsTrue(service.ColorSchemes.Count > 0);
    }

    [TestMethod]
    public void ThemeServiceHasDefaultTheme()
    {
        var service = new ThemeService();
        Assert.IsTrue(service.ColorSchemes.ContainsKey(Theme.Default));
    }

    [TestMethod]
    public void ThemeServiceHasDarkTheme()
    {
        var service = new ThemeService();
        Assert.IsTrue(service.ColorSchemes.ContainsKey(Theme.Dark));
    }

    [TestMethod]
    public void ThemeServiceCurrentColorSchemeDefault()
    {
        var service = new ThemeService();
        service.Theme = Theme.Default;
        var colorScheme = service.CurrentColorScheme;
        Assert.IsNotNull(colorScheme);
    }

    [TestMethod]
    public void ThemeServiceCurrentColorSchemeDark()
    {
        var service = new ThemeService();
        service.Theme = Theme.Dark;
        var colorScheme = service.CurrentColorScheme;
        Assert.IsNotNull(colorScheme);
        Assert.AreEqual(Theme.Dark, colorScheme.Theme);
    }

    [TestMethod]
    public void ThemeServiceSetTheme()
    {
        var service = new ThemeService();
        service.Theme = Theme.Dark;
        Assert.AreEqual(Theme.Dark, service.Theme);
    }

    [TestMethod]
    public void ThemeServiceDefaultColorSchemeBackground()
    {
        var service = new ThemeService();
        service.Theme = Theme.Default;
        var colorScheme = service.CurrentColorScheme;
        Assert.AreEqual(Color.Blue, colorScheme.Background);
    }

    [TestMethod]
    public void ThemeServiceDarkColorSchemeBackground()
    {
        var service = new ThemeService();
        service.Theme = Theme.Dark;
        var colorScheme = service.CurrentColorScheme;
        Assert.AreEqual(Color.Black, colorScheme.Background);
    }

    [TestMethod]
    public void ThemeServiceAddCustomTheme()
    {
        var service = new ThemeService();
        var customScheme = new ColorScheme 
        { 
            Theme = Theme.Default,
            Background = Color.Green,
            Foreground = Color.Yellow
        };
        service.ColorSchemes[Theme.Default] = customScheme;
        service.Theme = Theme.Default;

        Assert.AreEqual(Color.Green, service.CurrentColorScheme.Background);
    }

    [TestMethod]
    public void ThemeServiceFallbackToFirstScheme()
    {
        var service = new ThemeService();
        service.Theme = (Theme)999; // Non-existent theme

        var colorScheme = service.CurrentColorScheme;
        Assert.IsNotNull(colorScheme);
    }

    [TestMethod]
    public void ThemeServiceColorSchemesIsMutable()
    {
        var service = new ThemeService();
        var initialCount = service.ColorSchemes.Count;

        var newScheme = new ColorScheme();
        service.ColorSchemes[(Theme)1000] = newScheme;

        Assert.AreEqual(initialCount + 1, service.ColorSchemes.Count);
    }

    [TestMethod]
    public void TrySetThemeSwitchesTheCurrentScheme()
    {
        var themeService = new ThemeService();

        Assert.IsTrue(themeService.TrySetTheme(Theme.Dark));

        Assert.AreEqual(Theme.Dark, themeService.CurrentColorScheme.Theme);
    }

    [TestMethod]
    public void AServiceWithAFixedSchemeCannotSwitch()
    {
        IThemeService themeService = new iConsole.Test.Core.TestThemeService();

        Assert.IsFalse(themeService.TrySetTheme(Theme.Dark));
    }
}

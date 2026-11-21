using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Test.Services;

[TestClass]
public class ThemeServiceTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ThemeServiceConstructor()
    {
        var service = new ThemeService();
        Assert.IsNotNull(service);
        Assert.IsNotNull(service.ColorSchemes);
        Assert.IsTrue(service.ColorSchemes.Count > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThemeServiceHasDefaultTheme()
    {
        var service = new ThemeService();
        Assert.IsTrue(service.ColorSchemes.ContainsKey(Theme.Default));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThemeServiceHasDarkTheme()
    {
        var service = new ThemeService();
        Assert.IsTrue(service.ColorSchemes.ContainsKey(Theme.Dark));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThemeServiceCurrentColorSchemeDefault()
    {
        var service = new ThemeService();
        service.Theme = Theme.Default;
        var colorScheme = service.CurrentColorScheme;
        Assert.IsNotNull(colorScheme);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThemeServiceCurrentColorSchemeDark()
    {
        var service = new ThemeService();
        service.Theme = Theme.Dark;
        var colorScheme = service.CurrentColorScheme;
        Assert.IsNotNull(colorScheme);
        Assert.AreEqual(Theme.Dark, colorScheme.Theme);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThemeServiceSetTheme()
    {
        var service = new ThemeService();
        service.Theme = Theme.Dark;
        Assert.AreEqual(Theme.Dark, service.Theme);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThemeServiceDefaultColorSchemeBackground()
    {
        var service = new ThemeService();
        service.Theme = Theme.Default;
        var colorScheme = service.CurrentColorScheme;
        Assert.AreEqual(Color.Blue, colorScheme.Background);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThemeServiceDarkColorSchemeBackground()
    {
        var service = new ThemeService();
        service.Theme = Theme.Dark;
        var colorScheme = service.CurrentColorScheme;
        Assert.AreEqual(Color.Black, colorScheme.Background);
    }

    [Timeout(60_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    public void ThemeServiceFallbackToFirstScheme()
    {
        var service = new ThemeService();
        service.Theme = (Theme)999; // Non-existent theme

        var colorScheme = service.CurrentColorScheme;
        Assert.IsNotNull(colorScheme);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThemeServiceColorSchemesIsMutable()
    {
        var service = new ThemeService();
        var initialCount = service.ColorSchemes.Count;

        var newScheme = new ColorScheme();
        service.ColorSchemes[(Theme)1000] = newScheme;

        Assert.AreEqual(initialCount + 1, service.ColorSchemes.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TrySetThemeSwitchesTheCurrentScheme()
    {
        var themeService = new ThemeService();

        Assert.IsTrue(themeService.TrySetTheme(Theme.Dark));

        Assert.AreEqual(Theme.Dark, themeService.CurrentColorScheme.Theme);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThemeServiceHasMainframeTheme()
    {
        var service = new ThemeService();
        Assert.IsTrue(service.ColorSchemes.ContainsKey(Theme.Mainframe));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MainframeThemeIsBlackWithGreenText()
    {
        var themeService = new ThemeService();

        Assert.IsTrue(themeService.TrySetTheme(Theme.Mainframe));

        var scheme = themeService.CurrentColorScheme;
        Assert.AreEqual(Theme.Mainframe, scheme.Theme);
        Assert.AreEqual(Color.Black, scheme.Background);
        Assert.AreEqual(Color.Green, scheme.Foreground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MainframeThemeUsesOnlyGreensAndBlackExceptErrors()
    {
        var scheme = new ThemeService().ColorSchemes[Theme.Mainframe];
        Color[] greenFamily = [Color.Black, Color.Green, Color.DarkGreen];

        Color[] fields =
        [
            scheme.Background, scheme.Foreground, scheme.SelectedForeground, scheme.SelectedBackground,
            scheme.SelectedFocusedForeground, scheme.SelectedFocusedBackground, scheme.DisabledForeground,
            scheme.DisabledSelectedForeground, scheme.DisabledBackground, scheme.ButtonBackground,
            scheme.DisabledButtonBackground,
        ];
        foreach (var color in fields)
        {
            CollectionAssert.Contains(greenFamily, color);
        }

        Assert.AreEqual(Color.Red, scheme.Error, "errors stay red so they stand out from the green text");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MainframeThemeFocusedSelectionIsBrighterThanUnfocused()
    {
        var scheme = new ThemeService().ColorSchemes[Theme.Mainframe];

        Assert.AreEqual(Color.Green, scheme.SelectedFocusedBackground);
        Assert.AreEqual(Color.DarkGreen, scheme.SelectedBackground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AServiceWithAFixedSchemeCannotSwitch()
    {
        IThemeService themeService = new iConsole.Test.Core.TestThemeService();

        Assert.IsFalse(themeService.TrySetTheme(Theme.Dark));
    }
}

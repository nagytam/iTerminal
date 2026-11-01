using System.Resources;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Localization;
using iTerminal.Core.Services;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Controls.StatusBar.Test;

/// <summary>The hot key bar in the language of the localizer: the label changes, the key part does not.</summary>
[TestClass]
public class HotKeysLocalizationTest
{
    private sealed class TestHotKeys(ILocalizer localizer) : HotKeys(
        null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        NullSettingsService.Instance, null!, TestTheme(), localizer: localizer);

    private static Localizer CreateLocalizer() =>
        new([new ResourceLocalizationSource(new ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(HotKeys).Assembly))]);

    private static List<string> Titles(HotKeys hotKeys) =>
        [.. hotKeys.Controls.AsSnapshot().OfType<Button>().Select(b => b.Title)];

    [Timeout(60_000)]
    [TestMethod]
    public void EnglishIsLeftAsItWasBuilt()
    {
        var hotKeys = new TestHotKeys(CreateLocalizer());

        CollectionAssert.Contains(Titles(hotKeys), "F1-Help");
        CollectionAssert.Contains(Titles(hotKeys), "F5-Copy");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ChangingTheLanguageRelabelsTheButtonsOnTheNextIdlePass()
    {
        Localizer localizer = CreateLocalizer();
        var hotKeys = new TestHotKeys(localizer);
        var parent = new Control(TestTheme()) { ViewSize = new Size(400, 1) };
        parent.AddControl(hotKeys);

        localizer.SetLanguage("de");
        hotKeys.OnIdle();

        CollectionAssert.Contains(Titles(hotKeys), "F1-Hilfe");
        CollectionAssert.Contains(Titles(hotKeys), "F10-Ende");
        CollectionAssert.Contains(Titles(hotKeys), "Ctrl-A-Rechner");
        CollectionAssert.DoesNotContain(Titles(hotKeys), "F1-Help");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SwitchingBackToEnglishRestoresTheOriginalTitles()
    {
        Localizer localizer = CreateLocalizer();
        var hotKeys = new TestHotKeys(localizer);
        var parent = new Control(TestTheme()) { ViewSize = new Size(400, 1) };
        parent.AddControl(hotKeys);

        localizer.SetLanguage("fr");
        hotKeys.OnIdle();
        localizer.SetLanguage("en");
        hotKeys.OnIdle();

        CollectionAssert.Contains(Titles(hotKeys), "F1-Help");
        CollectionAssert.Contains(Titles(hotKeys), "Ctrl-G-Git");
    }
}

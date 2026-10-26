using System.Resources;
using iConsole.Controls;
using iConsole.Localization;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>Ctrl+Shift+F3: pick the language of the interface; it applies at once and is saved in the settings.</summary>
[TestClass]
public class LanguageDialogTest
{
    private static Localizer CreateLocalizer(ISettingsService settings) =>
        new(
            [new ResourceLocalizationSource(new ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(LanguageDialog).Assembly))],
            new SettingsLanguageStore(settings));

    [TestMethod]
    public async Task ListsEveryLanguageByItsOwnNameAndMarksTheOneInUse()
    {
        var dialog = new LanguageDialog(CreateLocalizer(new SettingsService()), TestTheme());
        await ShowAsync(dialog);

        var screen = Render(dialog);

        foreach (string name in new[] { "Deutsch", "Français", "Español", "Italiano", "Magyar" })
        {
            AssertScreenContains(screen, name);
        }

        AssertScreenContains(screen, "English");
        AssertScreenContains(screen, "•");
        AssertScreenContains(screen, "Enter:apply  Esc:close");
    }

    [TestMethod]
    public async Task EnterAppliesTheLanguageUnderTheCursorSavesItAndCloses()
    {
        var settings = new SettingsService();
        var localizer = CreateLocalizer(settings);
        var dialog = new LanguageDialog(localizer, TestTheme());
        var root = await ShowAsync(dialog);
        int raised = 0;
        localizer.LanguageChanged += (_, _) => raised++;

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.AreEqual("de", localizer.LanguageCode);
        Assert.AreEqual("de", settings.Get(iTerminal.Core.Data.Settings.Language));
        Assert.AreEqual(1, raised);
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog), "the dialog closes");
    }

    [TestMethod]
    public async Task TheCursorStartsOnTheLanguageInUseAndTheDialogSpeaksIt()
    {
        var localizer = CreateLocalizer(new SettingsService());
        localizer.SetLanguage("hu");
        var dialog = new LanguageDialog(localizer, TestTheme());
        await ShowAsync(dialog);
        dialog.Load();

        Assert.AreEqual("hu", dialog.Selected.Code);
        Assert.AreEqual("Nyelv", dialog.Title);
        AssertScreenContains(Render(dialog), "Enter:alkalmaz  Esc:bezárás");
    }

    [TestMethod]
    public async Task EscapeClosesWithoutChangingTheLanguage()
    {
        var localizer = CreateLocalizer(new SettingsService());
        var dialog = new LanguageDialog(localizer, TestTheme());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.AreEqual("en", localizer.LanguageCode);
    }
}

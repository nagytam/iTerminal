using iConsole.Localization;
using iTerminal.Core.Data;
using iTerminal.Core.Services;

namespace iTerminal.Test;

/// <summary>The language selector used the way a person uses it: open it, pick a language, see the screen change.</summary>
[TestClass]
public class LanguageTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    private async Task OpenSelectorAsync()
    {
        Assert.IsTrue(await _app.PressAsync("Ctrl+Shift+F3"));
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Dialogs.LanguageDialog, "the language dialog to open");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task EnglishIsTheDefaultAndTheSelectorListsEveryLanguageByItsOwnName()
    {
        await _app.WaitForTextAsync("F1-Help");

        await OpenSelectorAsync();

        foreach (string name in new[] { "English", "Deutsch", "Français", "Español", "Italiano", "Magyar" })
        {
            await _app.WaitForTextAsync(name);
        }

        await _app.WaitForTextAsync("Enter:apply  Esc:close");
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        Assert.AreEqual("en", _app.Driver.GetRequiredService<ILocalizer>().LanguageCode);
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task PickingALanguageChangesTheFooterAndTheMenuAtOnceAndSavesTheChoice()
    {
        await OpenSelectorAsync();
        await _app.PressAsync("Down");
        await _app.PressAsync("Enter");
        await _app.WaitForNoDialogAsync();

        await _app.WaitForTextAsync("F1-Hilfe");
        await _app.WaitForTextAsync("Datei");
        await _app.WaitForTextGoneAsync("F1-Help");

        Assert.AreEqual("de", _app.Driver.GetRequiredService<ILocalizer>().LanguageCode);
        Assert.AreEqual("de", _app.Driver.GetRequiredService<ISettingsService>().Get(Settings.Language));
    }
}

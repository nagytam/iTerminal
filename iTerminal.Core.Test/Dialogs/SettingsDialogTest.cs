using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Controls.Tabs;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class SettingsDialogTest
{
    private static SettingsDialog Create() =>
        new(new SettingsService(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

    [TestMethod]
    public async Task HasItsPagesInOrder()
    {
        var dialog = Create();
        await ShowAsync(dialog);

        var tab = dialog.Controls.AsSnapshot().OfType<Tab>().Single();

        CollectionAssert.AreEqual(
            new[] { "1-Global ", "2-Network", "3-Indicators ", "4-Music ", "5-Documents ", "6-Panels ", "7-Background" },
            tab.TabPages.Select(p => p.Name).ToArray());
    }

    [TestMethod]
    public async Task ThePageItOpensOnIsNotBlank()
    {
        // It opened on an empty "Global" page.
        var dialog = Create();
        await ShowAsync(dialog);

        AssertScreenContains(Render(dialog), "Theme manager (Alt+F6)");
    }

    [TestMethod]
    public async Task EscapeClosesIt()
    {
        var dialog = Create();
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }
}

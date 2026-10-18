using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Controls;
using iTerminal.Core.Test.Controls.MenuBar;

namespace iTerminal.Core.Controls.MenuBar.Test;

/// <summary>The Tools menu's entries, in order. Their shortcuts are checked by <see cref="MenuShortcutTest"/>.</summary>
[TestClass]
public class ToolsMenuItemTest
{
    [TestMethod]
    public void ListsItsEntriesInOrder()
    {
        var menu = MenuShortcutTest.Menus().Single(m => m.Text == "Tools");

        var entries = menu.Controls.AsSnapshot().OfType<MenuItem>()
            .Where(m => m.Text != "-")
            .Select(m => m.Text.Split('\t')[0])
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Terminal", "Disk Manager", "Network Manager", "iPhone Browser", "Music Player", "Theme Manager", "Settings", "Task Manager", "Process Manager", "System Info" }, entries);
    }

    [TestMethod]
    public void TheLargerFeaturesAreListedWithTheirOwnKeys()
    {
        // No menu led to the Git client, the document search, the calculator and the rest; they
        // were reachable only through hot keys nothing on screen named.
        var theme = iTerminal.Core.Test.Helper.UiTestHelper.TestTheme();
        var menu = new ToolsMenuItem(
            null!, null!, null!, null!, Moq.Mock.Of<iConsole.Interfaces.IContentModelService>(), null!, null!, null!, null!, null!, theme,
            calculatorDialog: new iTerminal.Core.Dialogs.Calculator.CalculatorDialog(theme),
            unitConverterDialog: new iTerminal.Core.Dialogs.UnitConverter.UnitConverterDialog(
                Moq.Mock.Of<iConsole.Services.IBeepService>(), new iConsole.Services.DateTimeProviderService(), theme));

        var entries = menu.Controls.AsSnapshot().OfType<MenuItem>().Select(m => m.Text).ToList();

        CollectionAssert.Contains(entries, "Calculator" + (char)9 + "Ctrl+A");
        CollectionAssert.Contains(entries, "Unit Converter" + (char)9 + "Ctrl+E");
    }

    [TestMethod]
    public void TheTranslatorIsListedWithItsKey()
    {
        var theme = iTerminal.Core.Test.Helper.UiTestHelper.TestTheme();
        var translator = new iTerminal.Core.Dialogs.Translator.TranslatorDialog(
            Moq.Mock.Of<iTerminal.Core.Services.Translation.ITranslationService>(),
            Moq.Mock.Of<iTerminal.Core.Services.ISettingsService>(),
            Microsoft.Extensions.Options.Options.Create(new iTerminal.Core.Services.Translation.TranslationOptions()),
            theme);
        var menu = new ToolsMenuItem(
            null!, null!, null!, null!, Moq.Mock.Of<iConsole.Interfaces.IContentModelService>(), null!, null!, null!, null!, null!, theme,
            translatorDialog: translator);

        var entries = menu.Controls.AsSnapshot().OfType<MenuItem>().Select(m => m.Text).ToList();

        Assert.IsTrue(entries.Any(e => e.StartsWith("Translator" + (char)9, StringComparison.Ordinal) && e.Contains("F4", StringComparison.Ordinal)),
            string.Join(" | ", entries));
    }

    [TestMethod]
    public void RuntimeDependenciesIsListedWithoutAHotKey()
    {
        // Reached from here and the command palette only, so the label carries no key and no tab.
        var theme = iTerminal.Core.Test.Helper.UiTestHelper.TestTheme();
        var service = Moq.Mock.Of<iTerminal.Core.Services.RuntimeDependencies.IRuntimeDependencyService>();
        var menu = new ToolsMenuItem(
            null!, null!, null!, null!, Moq.Mock.Of<iConsole.Interfaces.IContentModelService>(), null!, null!, null!, null!, null!, theme,
            runtimeDependencyDialog: new iTerminal.Core.Dialogs.RuntimeDependencyDialog(service, theme));

        var entries = menu.Controls.AsSnapshot().OfType<MenuItem>().Select(m => m.Text).ToList();

        CollectionAssert.Contains(entries, "Runtime Dependencies");
    }
}

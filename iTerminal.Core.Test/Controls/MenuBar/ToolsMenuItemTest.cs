using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Controls;
using iTerminal.Core.Test.Controls.MenuBar;

namespace iTerminal.Core.Controls.MenuBar.Test;

/// <summary>The Tools menu's entries, in order. Their shortcuts are checked by <see cref="MenuShortcutTest"/>.</summary>
[TestClass]
public class ToolsMenuItemTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ListsItsEntriesInOrder()
    {
        var menu = MenuShortcutTest.Menus().Single(m => m.Text == "Tools");

        var entries = menu.Controls.AsSnapshot().OfType<MenuItem>()
            .Where(m => m.Text != "-")
            .Select(m => m.Text.Split('\t')[0])
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Terminal", "Disk Manager", "Network Manager", "iPhone Browser", "Theme Manager", "Settings", "Task Manager", "Process Manager", "System Info", "Media" }, entries);
    }

    [Timeout(60_000)]
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

        var entries = Entries(menu).Select(m => m.Text).ToList();

        CollectionAssert.Contains(entries, "Calculator" + (char)9 + "Ctrl+A");
        CollectionAssert.Contains(entries, "Unit Converter" + (char)9 + "Ctrl+E");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheTranslatorIsListedWithItsKey()
    {
        var theme = iTerminal.Core.Test.Helper.UiTestHelper.TestTheme();
        var translator = new iTerminal.Widgets.Dialogs.Translator.TranslatorDialog(
            Moq.Mock.Of<iTerminal.Widgets.Services.Translation.ITranslationService>(),
            Moq.Mock.Of<iTerminal.Core.Services.ISettingsService>(),
            Microsoft.Extensions.Options.Options.Create(new iTerminal.Widgets.Services.Translation.TranslationOptions()),
            theme);
        var menu = new ToolsMenuItem(
            null!, null!, null!, null!, Moq.Mock.Of<iConsole.Interfaces.IContentModelService>(), null!, null!, null!, null!, null!, theme,
            widgetContributions: [new iTerminal.Widgets.Application.WidgetToolsMenuEntry("office", new iTerminal.Widgets.Commands.TranslatorDialogCommand(translator))]);

        var entries = Entries(menu).Select(m => m.Text).ToList();

        Assert.IsTrue(entries.Any(e => e.StartsWith("Translator" + (char)9, StringComparison.Ordinal) && e.Contains("F4", StringComparison.Ordinal)),
            string.Join(" | ", entries));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RuntimeDependenciesIsListedWithoutAHotKey()
    {
        // Reached from here and the command palette only, so the label carries no key and no tab.
        var theme = iTerminal.Core.Test.Helper.UiTestHelper.TestTheme();
        var service = Moq.Mock.Of<iTerminal.Core.Services.RuntimeDependencies.IRuntimeDependencyService>();
        var menu = new ToolsMenuItem(
            null!, null!, null!, null!, Moq.Mock.Of<iConsole.Interfaces.IContentModelService>(), null!, null!, null!, null!, null!, theme,
            runtimeDependencyDialog: new iTerminal.Core.Dialogs.RuntimeDependencyDialog(service, theme));

        var entries = Entries(menu).Select(m => m.Text).ToList();

        CollectionAssert.Contains(entries, "Runtime Dependencies");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RelatedToolsSitInSubmenusUnderTheirTitle()
    {
        var theme = iTerminal.Core.Test.Helper.UiTestHelper.TestTheme();
        var menu = new ToolsMenuItem(
            null!, null!, null!, null!, Moq.Mock.Of<iConsole.Interfaces.IContentModelService>(), null!, null!, null!, null!, null!, theme,
            calculatorDialog: new iTerminal.Core.Dialogs.Calculator.CalculatorDialog(theme),
            runtimeDependencyDialog: new iTerminal.Core.Dialogs.RuntimeDependencyDialog(
                Moq.Mock.Of<iTerminal.Core.Services.RuntimeDependencies.IRuntimeDependencyService>(), theme));

        var topLevel = menu.Controls.AsSnapshot().OfType<MenuItem>().ToList();
        var office = topLevel.Single(m => m.Text == "Office");
        var system = topLevel.Single(m => m.Text == "System");

        // The title is the drop-down's own row: it carries the submenu arrow and no shortcut.
        Assert.AreEqual("▶", office.HotKeyText);
        CollectionAssert.AreEqual(new[] { "Calculator" + (char)9 + "Ctrl+A" }, office.Controls.AsSnapshot().OfType<MenuItem>().Select(m => m.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "Runtime Dependencies" }, system.Controls.AsSnapshot().OfType<MenuItem>().Select(m => m.Text).ToArray());
        Assert.IsFalse(topLevel.Any(m => m.Text == "Calculator" + (char)9 + "Ctrl+A"), "the calculator belongs in the Office submenu, not the top level");
    }

    /// <summary>Every entry of the menu in order, including those inside a submenu; a submenu's own title is not one.</summary>
    private static IEnumerable<MenuItem> Entries(MenuItem menu)
    {
        foreach (MenuItem item in menu.Controls.AsSnapshot().OfType<MenuItem>().Where(m => !m.IsSeparator))
        {
            if (item.Controls.AsSnapshot().OfType<MenuItem>().Any())
            {
                foreach (MenuItem child in Entries(item))
                {
                    yield return child;
                }
            }
            else
            {
                yield return item;
            }
        }
    }
}

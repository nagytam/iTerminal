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
}

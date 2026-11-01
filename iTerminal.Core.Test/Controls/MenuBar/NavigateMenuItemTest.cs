using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Controls;
using iTerminal.Core.Test.Controls.MenuBar;

namespace iTerminal.Core.Controls.MenuBar.Test;

/// <summary>The Navigate menu's entries, in order. Their shortcuts are checked by <see cref="MenuShortcutTest"/>.</summary>
[TestClass]
public class NavigateMenuItemTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ListsItsEntriesInOrder()
    {
        var menu = MenuShortcutTest.Menus().Single(m => m.Text == "Navigate");

        var entries = menu.Controls.AsSnapshot().OfType<MenuItem>()
            .Where(m => m.Text != "-")
            .Select(m => m.Text.Split('\t')[0])
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Go To Folder", "Switch Panels", "Sort By…", "Git History", "Docker Containers", "Recycle Bin", "Shell", "Command Log" }, entries);
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Controls;
using iTerminal.Core.Test.Controls.MenuBar;

namespace iTerminal.Core.Controls.MenuBar.Test;

/// <summary>The Edit menu's entries, in order. Their shortcuts are checked by <see cref="MenuShortcutTest"/>.</summary>
[TestClass]
public class EditMenuItemTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ListsItsEntriesInOrder()
    {
        var menu = MenuShortcutTest.Menus().Single(m => m.Text == "Edit");

        var entries = menu.Controls.AsSnapshot().OfType<MenuItem>()
            .Where(m => m.Text != "-")
            .Select(m => m.Text.Split('\t')[0])
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Copy", "Move", "Rename", "Delete", "Mark", "Mark by Pattern", "Unmark by Pattern", "Invert Marks", "Mark All", "Unmark All", "Compare Folders" }, entries);
    }
}

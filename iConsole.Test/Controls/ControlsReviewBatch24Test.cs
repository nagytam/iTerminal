using iConsole.Controls;
using iConsole.Data;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

/// <summary>The help overlay lists the keys of what has focus; the popup list, the open menu and the popup took keys it never showed.</summary>
[TestClass]
public class ControlsReviewBatch24Test
{
    private readonly TestThemeService _theme = new();

    private static string[] Keys(IEnumerable<(string Key, string Description)> hints) => [.. hints.Select(h => h.Key)];

    [TestMethod]
    public void PopupListView_KeyHints_NameTheListKeys()
    {
        PopupListView view = new(_theme);

        CollectionAssert.AreEqual(new[] { "Up/Down", "PageUp/PageDown", "Home/End", "Enter/Space", "Esc" }, Keys(view.GetKeyHints()));
    }

    [TestMethod]
    public void MenuItem_KeyHints_AreOfferedOnlyWhileTheMenuIsOpen()
    {
        MenuItem child = new("Child", null, new MenuItem[] { }, _theme);
        MenuItem parent = new("Parent", null, new[] { child }, _theme);

        Assert.AreEqual(0, parent.GetKeyHints().Count());

        parent.OpenMenu();

        CollectionAssert.IsSubsetOf(new[] { "Up/Down", "Enter/Space", "Right", "Left", "Esc" }, Keys(parent.GetKeyHints()));
    }

    [TestMethod]
    public void Popup_KeyHints_AreEmptyUntilItIsOpen()
    {
        Popup popup = new(_theme) { ViewSize = new Size(20, 6) };

        Assert.AreEqual(0, popup.GetKeyHints().Count());
    }

    [TestMethod]
    public async Task Popup_KeyHints_NameEscWhileOpen_AndNothingWhenEscDoesNotClose()
    {
        Window root = new(_theme) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        CheckBox owner = new(_theme) { Position = new Position(5, 5), ViewSize = new Size(10, 1) };
        root.AddControl(owner);
        Popup popup = new(_theme) { ViewSize = new Size(20, 6) };

        await popup.ShowAsync(owner);
        CollectionAssert.AreEqual(new[] { "Esc" }, Keys(popup.GetKeyHints()));

        popup.CloseOnEscape = false;
        Assert.AreEqual(0, popup.GetKeyHints().Count());
    }
}

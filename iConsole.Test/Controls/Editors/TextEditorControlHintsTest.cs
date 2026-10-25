using iConsole.Controls.Editors;
using iConsole.Display;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Controls.Editors;

[TestClass]
public class TextEditorControlHintsTest
{
    private static TextEditorControl CreateEditor()
    {
        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentColorScheme).Returns(new ColorScheme());
        return new TextEditorControl(theme.Object);
    }

    [TestMethod]
    public void KeyHints_OfferTheEditsWhileEditable()
    {
        var keys = CreateEditor().GetKeyHints().Select(h => h.Key).ToList();

        CollectionAssert.Contains(keys, "Ctrl+Z");
        CollectionAssert.Contains(keys, "Ctrl+R");
        CollectionAssert.Contains(keys, "Tab / Shift+Tab");
        CollectionAssert.Contains(keys, "Ctrl+K");
        CollectionAssert.Contains(keys, "Ctrl+V");
        CollectionAssert.Contains(keys, "Ctrl+X");
    }

    [TestMethod]
    public void KeyHints_OfferOnlyTheMovesAndSaveWhenReadOnly()
    {
        var editor = CreateEditor();
        editor.IsReadOnly = true;

        var keys = editor.GetKeyHints().Select(h => h.Key).ToList();

        CollectionAssert.DoesNotContain(keys, "Ctrl+Z");
        CollectionAssert.DoesNotContain(keys, "Ctrl+Y");
        CollectionAssert.DoesNotContain(keys, "Ctrl+R");
        CollectionAssert.DoesNotContain(keys, "Tab / Shift+Tab");
        CollectionAssert.DoesNotContain(keys, "Ctrl+V");
        CollectionAssert.DoesNotContain(keys, "Ctrl+X");
        CollectionAssert.Contains(keys, "Ctrl+C");
        CollectionAssert.Contains(keys, "F5");
        CollectionAssert.Contains(keys, "Ctrl+S / F2");
    }
}

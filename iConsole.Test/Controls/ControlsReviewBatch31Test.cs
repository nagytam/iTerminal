using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Helpers and inserts that threw for null or a negative length (found by fuzzing the public and internal helpers).</summary>
[TestClass]
public class ControlsReviewBatch31Test
{
    private readonly TestThemeService _theme = new();

    /// <summary>Calls an internal static method of iConsole (the assembly shows its internals to no test project).</summary>
    private static object? Internal(Type type, string name, string? argument)
        => type.GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, [argument]);

    [TestMethod]
    public void TextBox_InsertText_OfNull_ChangesNothing()
    {
        TextBox box = new(Mock.Of<IBeepService>(), new DateTimeProviderService(), _theme) { Text = "abc" };

        box.InsertText(null);

        Assert.AreEqual("abc", box.Text);
    }

    [TestMethod]
    public void TextEditor_InsertText_OfNull_ChangesNothing()
    {
        TextEditorControl editor = new(_theme);
        int before = editor.TextModel.Lines.Count;

        editor.InsertText(null);

        Assert.AreEqual(before, editor.TextModel.Lines.Count);
    }

    [TestMethod]
    public void TerminalSequences_Sanitize_WithANegativeLength_IsEmpty()
    {
        Assert.AreEqual(string.Empty, TerminalSequences.Sanitize("title", -1, keepSemicolons: true));
    }

    [TestMethod]
    public void OfficeTextRenderer_IsDateFormat_OfNull_IsFalse()
    {
        Assert.AreEqual(false, Internal(typeof(OfficeTextRenderer), "IsDateFormat", null));
    }

    [TestMethod]
    public void Hyperlinks_Sanitize_OfNull_IsEmpty()
    {
        Assert.AreEqual(string.Empty, Internal(typeof(Hyperlinks), "Sanitize", null));
    }

    [TestMethod]
    public void ClipboardKeys_NormalizeLineBreaks_OfNull_IsEmpty()
    {
        Assert.AreEqual(string.Empty, ClipboardKeys.NormalizeLineBreaks(null));
        Assert.AreEqual("a\nb", ClipboardKeys.NormalizeLineBreaks("a\r\nb"));
    }
}

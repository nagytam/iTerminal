using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Extensions;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Json;
using iConsole.Services;
using iConsole.Test.Core;
using iConsole.Xml;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Key hints for the viewer and editor dialogs, and tests for helpers that had none.</summary>
[TestClass]
public class ControlsReviewBatch34Test
{
    private readonly TestThemeService _theme = new();

    private static string[] Keys(IEnumerable<(string Key, string Description)> hints) => [.. hints.Select(h => h.Key)];

    [TestMethod]
    public void FileViewerDialog_KeyHints_NameCloseHexAndCodePage()
    {
        FileViewerDialog dialog = new(Mock.Of<IContentModelService>(), Mock.Of<IFileToTextConverterService>(), Mock.Of<IBeepService>(),
            new DateTimeProviderService(), _theme);

        CollectionAssert.AreEqual(new[] { "Esc", "F4", "F8" }, Keys(dialog.GetKeyHints()));
    }

    [TestMethod]
    public void TextFileEditorDialog_KeyHints_NameSaveIndentAndCodePage()
    {
        TextFileEditorDialog dialog = new(Mock.Of<IContentModelService>(), Mock.Of<IFileToTextConverterService>(), Mock.Of<IBeepService>(),
            new DateTimeProviderService(), _theme);

        CollectionAssert.AreEqual(new[] { "Ctrl+S", "Tab", "F8" }, Keys(dialog.GetKeyHints()));
    }

    [TestMethod]
    public void GoToLineDialog_KeyHints_NameTypeEnterAndEsc()
    {
        GoToLineDialog dialog = new(new TextViewerControl(false, _theme), Mock.Of<IBeepService>(), new DateTimeProviderService(), _theme);

        CollectionAssert.AreEqual(new[] { "0-9", "Enter", "Esc" }, Keys(dialog.GetKeyHints()));
    }

    [TestMethod]
    public void GoToOffsetDialog_KeyHints_NameTypeEnterAndEsc()
    {
        GoToOffsetDialog dialog = new(new HexViewer(_theme), Mock.Of<IBeepService>(), new DateTimeProviderService(), _theme);

        CollectionAssert.AreEqual(new[] { "0-9, 0x.., ..h", "Enter", "Esc" }, Keys(dialog.GetKeyHints()));
    }

    [TestMethod]
    public void UnsavedChangesDialog_KeyHints_NameSaveDiscardCancel()
    {
        UnsavedChangesDialog dialog = new("notes.txt", _theme);

        CollectionAssert.AreEqual(new[] { "S", "D", "C" }, Keys(dialog.GetKeyHints()));
    }

    [TestMethod]
    public async Task UnsavedChangesDialog_TheLetterAnswers_AndClosingWithoutAnAnswerIsCancel()
    {
        UnsavedChangesDialog saved = new("notes.txt", _theme);
        await saved.HandleKeyEventAsync(new KeyEvent('s', ConsoleKey.S, false, false, false));
        Assert.AreEqual(UnsavedChoice.Save, await saved.Choice);

        UnsavedChangesDialog discarded = new("notes.txt", _theme);
        await discarded.HandleKeyEventAsync(new KeyEvent('D', ConsoleKey.D, false, false, false));
        Assert.AreEqual(UnsavedChoice.Discard, await discarded.Choice);

        UnsavedChangesDialog escaped = new("notes.txt", _theme);
        escaped.Close();
        Assert.AreEqual(UnsavedChoice.Cancel, await escaped.Choice);
    }

    [TestMethod]
    public void ContextMenu_KeyHints_AreOfferedOnlyWhileOpen()
    {
        ContextMenu menu = new(Mock.Of<IPopoverService>(), _theme);

        Assert.AreEqual(0, menu.GetKeyHints().Count());
    }

    [TestMethod]
    public void ReplaceTextDialog_CanBeBuiltForAnEditor()
    {
        ReplaceTextDialog dialog = new(new TextEditorControl(_theme), Mock.Of<IBeepService>(), new DateTimeProviderService(), _theme);

        Assert.AreEqual("Replace text", dialog.Title);
    }

    [TestMethod]
    public void TextBoxExtensions_WithAutoCompletion_AttachesTheListOnlyWhenThereAreCompletions()
    {
        TextBox withValues = new(Mock.Of<IBeepService>(), new DateTimeProviderService(), _theme) { ViewSize = new Size(20, 1) };
        TextBox without = new(Mock.Of<IBeepService>(), new DateTimeProviderService(), _theme) { ViewSize = new Size(20, 1) };

        withValues.WithAutoCompletion(["alpha", "beta"]);
        without.WithAutoCompletion(Array.Empty<string>());

        Assert.IsNotNull(withValues.AutoCompletion);
        Assert.IsTrue(withValues.AutoCompletion.IsVisible);
        Assert.IsFalse(without.AutoCompletion!.IsVisible);
    }

    [TestMethod]
    public void ConcurrentListExtensions_FromList_ReplacesWhatWasThere()
    {
        ConcurrentList<string> list = ["old"];

        list.FromList(["a", "b"]);

        CollectionAssert.AreEqual(new[] { "a", "b" }, list.AsSnapshot().ToArray());
    }

    [TestMethod]
    public void JsonAndXmlFileTypes_RecogniseTheirExtensions_IgnoringCase()
    {
        Assert.IsTrue(JsonFileTypes.IsJsonFile(@"C:\data\Config.JSON"));
        Assert.IsFalse(JsonFileTypes.IsJsonFile("notes.txt"));
        Assert.IsFalse(JsonFileTypes.IsJsonFile(string.Empty));
        Assert.IsTrue(XmlFileTypes.IsXmlFile("App.csproj"));
        Assert.IsTrue(XmlFileTypes.IsXmlFile("logo.SVG"));
        Assert.IsFalse(XmlFileTypes.IsXmlFile("App.sln"));
        Assert.IsFalse(XmlFileTypes.IsXmlFile(null!));
    }
}

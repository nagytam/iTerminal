using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iConsole.Dialogs.Test;

/// <summary>
/// The editor's footer shows F6 as its read-only toggle, so F6 must mean read-only. Wrapping lines needs a key of its own:
/// F2 is save here, and the editor's text is never focusable, so the footer always takes F6 first and the wrap command on it was unreachable.
/// </summary>
[TestClass]
public class TextFileEditorHotKeysTest
{
    /// <summary>Shown the way the app shows it: a dialog only runs its own commands once it is enabled, by <c>ShowAsync</c>.</summary>
    private static async Task<TextFileEditorDialog> NewShownDialogAsync()
    {
        var dlg = new TextFileEditorDialog(Mock.Of<IContentModelService>(), Mock.Of<IFileToTextConverterService>(), Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(), new ThemeService());
        await dlg.ShowAsync();
        return dlg;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F6_TogglesReadOnly_WhenTheFooterHasTheFocus()
    {
        var dlg = await NewShownDialogAsync();
        var readOnlyBefore = dlg.TextEditorControl.IsReadOnly;

        await dlg.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F6, false, false, false));

        Assert.AreNotEqual(readOnlyBefore, dlg.TextEditorControl.IsReadOnly, "F6 is the footer's read-only key");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftF2_WrapsLinesInTheEditor_BecauseF2IsSave()
    {
        var dlg = await NewShownDialogAsync();
        var wrapBefore = dlg.TextEditorControl.WrapLines;

        await dlg.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F2, true, false, false));

        Assert.AreNotEqual(wrapBefore, dlg.TextEditorControl.WrapLines, "the wrap toggle needs a key that is neither F6 (read-only) nor F2 (save)");
    }
}

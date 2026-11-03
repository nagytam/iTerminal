using iConsole.Dialogs;
using iConsole.Localization;

namespace iTerminal.Core.Test.Commands;

/// <summary>The F1 overlay of a dialog names Close and Cancel in the language the user chose.</summary>
[TestClass]
public class DialogHintsLanguageTest
{
    private IDisposable? _language;

    [TestCleanup]
    public void Reset() => _language?.Dispose();

    [Timeout(60_000)]
    [TestMethod]
    public void NoDialogHint_SaysCloseOrCancelInEnglish_InGerman()
    {
        Localizer localizer = new();
        Assert.IsTrue(localizer.SetLanguage("de"));
        _language?.Dispose();
        _language = UiText.Scope(localizer);

        List<string> english = [];
        foreach (Type type in typeof(iTerminal.Core.Dialogs.SortDialog).Assembly.GetExportedTypes()
            .Where(t => typeof(Dialog).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (DialogHintsWholeTest.Resolve(type, 0) is not Dialog dialog)
            {
                continue;
            }

            try
            {
                if (dialog.GetKeyHints().Any(h => h.Description is "Close" or "Cancel" or "Confirm" or "Help" or "Back" or "Paste" or "Undo" or "Save" or "Stop"))
                {
                    english.Add(type.Name);
                }
            }
            catch (Exception)
            {
            }
        }

        Assert.AreEqual(0, english.Count, string.Join(", ", english));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheGermanHints_AreTheTranslatedWords()
    {
        Localizer localizer = new();
        localizer.SetLanguage("de");
        _language?.Dispose();
        _language = UiText.Scope(localizer);

        Assert.AreEqual("Schließen", UiText.Get("Common.Close", "Close"));
        Assert.AreEqual("Abbrechen", UiText.Get("Common.Cancel", "Cancel"));
    }
}

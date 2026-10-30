using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Localization;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Commands;

/// <summary>
/// A translated title is longer or shorter than the English one. Whatever the language, a dialog shown on a normal screen
/// still draws its whole title. The language is a static, so this runs on its own.
/// </summary>
[TestClass]
[DoNotParallelize]
public class DialogTitleFitsLanguageTest
{
    [TestCleanup]
    public void Reset() => UiText.Current = null;

    [TestMethod]
    [Timeout(300_000)]
    public async Task EveryDialog_DrawsItsWholeTitle_InEveryLanguage()
    {
        List<string> clipped = [];
        int shown = 0;
        foreach (LanguageInfo language in Localizer.SupportedLanguages)
        {
            Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(iTerminal.Core.Dialogs.SortDialog).Assembly))]);
            Assert.IsTrue(localizer.SetLanguage(language.Code));
            UiText.Current = localizer;

            foreach (Type type in typeof(iTerminal.Core.Dialogs.SortDialog).Assembly.GetExportedTypes()
                .Where(t => typeof(Dialog).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
                .OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                if (DialogHintsWholeTest.Resolve(type, 0) is not Dialog dialog || string.IsNullOrWhiteSpace(dialog.Title))
                {
                    continue;
                }

                try
                {
                    Size screen = new(80, 24);
                    Control root = await ShowAsync(dialog, screen);
                    shown++;
                    if (!Render(root, screen).Contains(dialog.Title))
                    {
                        clipped.Add($"{language.Code}:{type.Name} \"{dialog.Title}\"");
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        Assert.IsTrue(shown > 100, $"only {shown} dialogs could be shown");
        Assert.AreEqual(0, clipped.Count, string.Join("; ", clipped));
    }
}

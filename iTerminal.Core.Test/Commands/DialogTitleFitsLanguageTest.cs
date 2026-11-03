using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Localization;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Commands;

/// <summary>
/// A translated title is longer or shorter than the English one. Whatever the language, a dialog shown on a normal screen
/// still draws its whole title.
/// </summary>
[TestClass]
public class DialogTitleFitsLanguageTest
{
    private IDisposable? _language;

    [TestCleanup]
    public void Reset() => _language?.Dispose();

    /// <summary>One case per language, so that the languages are swept side by side.</summary>
    public static IEnumerable<object[]> LanguageCodes() => Localizer.SupportedLanguages.Select(language => new object[] { language.Code });

    [TestMethod]
    [Timeout(300_000)]
    [DynamicData(nameof(LanguageCodes), DynamicDataSourceType.Method)]
    public async Task EveryDialog_DrawsItsWholeTitle_InEveryLanguage(string code)
    {
        List<string> clipped = [];
        int shown = 0;
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(iTerminal.Core.Dialogs.SortDialog).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage(code));
        _language?.Dispose();
        _language = UiText.Scope(localizer);

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
                    clipped.Add($"{code}:{type.Name} \"{dialog.Title}\"");
                }
            }
            catch (Exception)
            {
            }
        }

        Assert.IsTrue(shown > 50, $"only {shown} dialogs could be shown in {code}");
        Assert.AreEqual(0, clipped.Count, string.Join("; ", clipped));
    }
}

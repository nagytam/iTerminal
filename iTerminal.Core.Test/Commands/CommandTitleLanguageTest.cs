using iConsole.Commands;
using iConsole.Localization;

namespace iTerminal.Core.Test.Commands;

/// <summary>The F1 help and the command palette name every command in the language the user chose.</summary>
[TestClass]
public class CommandTitleLanguageTest
{
    private IDisposable? _language;

    [TestCleanup]
    public void Reset() => _language?.Dispose();

    /// <summary>Names that are the same word in every language (products and games).</summary>
    private static readonly HashSet<string> SameInEveryLanguage =
        ["BitTorrent", "Git", "Git pull --rebase", "Go", "Shell", "Swiss Jass", "Tetris", "Terminal", "Tic-Tac-Toe", "Contacts", "Documents", "Signatures", "Type"];

    private static IEnumerable<Type> CommandTypes() =>
        new[] { typeof(iTerminal.Core.Dialogs.SortDialog).Assembly, typeof(Command).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(Command).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("de")]
    [DataRow("fr")]
    [DataRow("es")]
    [DataRow("it")]
    [DataRow("hu")]
    public void EveryCommandTitle_HasATranslation(string language)
    {
        Localizer localizer = new(
        [
            new ResourceLocalizationSource(new System.Resources.ResourceManager("iConsole.Localization.Resources.Strings", typeof(Localizer).Assembly)),
            new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(iTerminal.Core.Dialogs.SortDialog).Assembly)),
        ]);
        Assert.IsTrue(localizer.SetLanguage(language));
        _language?.Dispose();
        _language = UiText.Scope(localizer);

        HashSet<string> untranslated = [];
        int checkedTitles = 0;
        foreach (Type type in CommandTypes())
        {
            if (DialogHintsWholeTest.Resolve(type, 0) is not Command command)
            {
                continue;
            }

            string english;
            try
            {
                using (UiText.Scope(null))
                {
                    english = command.Title?.Trim() ?? string.Empty;
                }
            }
            catch (Exception)
            {
                continue;
            }

            // The viewers' hot-key bar captions carry their own key ("F2-Save"); they are not command names.
            if (english.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(english, @"^(F\d+-|\^|Ctrl-)") || SameInEveryLanguage.Contains(english))
            {
                continue;
            }

            checkedTitles++;
            if (string.Equals(UiText.Title(english), english, StringComparison.Ordinal))
            {
                untranslated.Add(english);
            }
        }

        Assert.IsTrue(checkedTitles > 100, $"only {checkedTitles} titles were looked at");
        Assert.AreEqual(0, untranslated.Count, $"{language}: {untranslated.Count} titles are still English: {string.Join(", ", untranslated.Order())}");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThePaletteFindsACommandByItsEnglishNameAndByItsGermanOne()
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(iTerminal.Core.Dialogs.SortDialog).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage("de"));
        using IDisposable language = UiText.Scope(localizer);
        Moq.Mock<ICommand> command = new();
        iTerminal.Core.Services.CommandPalette.PaletteEntry[] entries =
        [
            new(command.Object, UiText.Title("Compare folders"), string.Empty, string.Empty, "Compare folders"),
            new(command.Object, UiText.Title("Copy"), string.Empty, string.Empty, "Copy"),
        ];

        Assert.AreEqual("Ordner vergleichen", entries[0].Title);
        Assert.AreEqual("Compare folders", iTerminal.Core.Services.CommandPalette.CommandCatalog.Filter(entries, "ordner")[0].Name);
        Assert.AreEqual("Compare folders", iTerminal.Core.Services.CommandPalette.CommandCatalog.Filter(entries, "compare")[0].Name);
        Assert.AreEqual(0, iTerminal.Core.Services.CommandPalette.CommandCatalog.Filter(entries, "zzz").Count);
    }
}

using iConsole.Commands;
using iConsole.Localization;

namespace iTerminal.Core.Test.Commands;

/// <summary>The F1 help explains every command in the language the user chose: no description is left in English.</summary>
[TestClass]
public class CommandDescriptionLanguageTest
{
    private IDisposable? _language;

    [TestCleanup]
    public void Reset() => _language?.Dispose();

    private static IEnumerable<Type> CommandTypes() =>
        new[] { typeof(iTerminal.Core.Dialogs.SortDialog).Assembly, typeof(Command).Assembly }
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => typeof(Command).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("de")]
    [DataRow("fr")]
    [DataRow("es")]
    [DataRow("it")]
    [DataRow("hu")]
    public void EveryCommandDescription_HasATranslation(string language)
    {
        Localizer localizer = new(
        [
            new ResourceLocalizationSource(new System.Resources.ResourceManager("iConsole.Localization.Resources.Strings", typeof(Localizer).Assembly)),
            new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(iTerminal.Core.Dialogs.SortDialog).Assembly)),
        ]);
        Assert.IsTrue(localizer.SetLanguage(language));
        _language?.Dispose();
        _language = UiText.Scope(localizer);

        List<string> untranslated = [];
        int checkedCommands = 0;
        foreach (Type type in CommandTypes())
        {
            if (DialogHintsWholeTest.Resolve(type, 0) is not Command command)
            {
                continue;
            }

            string description;
            try
            {
                description = command.Description?.Trim() ?? string.Empty;
            }
            catch (Exception)
            {
                continue;
            }

            // A description that only repeats the title is not shown by the help, so it has nothing to translate.
            if (description.Length == 0 || string.Equals(description.TrimEnd('.'), command.Title, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A command that looks its own description up already speaks the language: it differs from its English form.
            string english;
            using (UiText.Scope(null))
            {
                english = command.Description?.Trim() ?? string.Empty;
            }

            if (!string.Equals(english, description, StringComparison.Ordinal))
            {
                continue;
            }

            checkedCommands++;
            if (string.Equals(UiText.Describe(description), description, StringComparison.Ordinal))
            {
                untranslated.Add($"{type.Name}: {description}");
            }
        }

        Assert.IsTrue(checkedCommands >= 100, $"only {checkedCommands} commands could be checked");
        Assert.AreEqual(0, untranslated.Count, $"{language}: {untranslated.Count} descriptions are still English:{Environment.NewLine}" + string.Join(Environment.NewLine, untranslated));
    }
}

namespace iConsole.Localization;

/// <summary>A language the user interface can be shown in: its culture code and its own name for itself.</summary>
/// <param name="Code">The culture code, <c>en</c>, <c>de</c>, ...</param>
/// <param name="NativeName">The name in that language ("Deutsch"), which is what a picker lists.</param>
public sealed record LanguageInfo(string Code, string NativeName)
{
    public LanguageInfo() : this("en", "English")
    {
    }
}

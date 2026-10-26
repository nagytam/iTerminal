using System.Globalization;

namespace iConsole.Localization;

/// <summary>
/// Turns a resource key into text in the language the user chose. English is the neutral language: a key
/// a language does not translate, or a language that is not there at all, shows the English text, and a key
/// no language knows shows the key itself, so a missing string is visible rather than empty.
///
/// <para>The language can change at run time. Anything that keeps text it has already looked up (a menu
/// label, a footer button) listens to <see cref="LanguageChanged"/> and looks it up again; the event may
/// arrive on any thread, so a control notes it and re-reads on its next idle pass.</para>
/// </summary>
public interface ILocalizer
{
    /// <summary>The culture the strings are looked up in.</summary>
    CultureInfo Culture { get; }

    /// <summary>The code of the current language: <c>en</c>, <c>de</c>, ...</summary>
    string LanguageCode { get; }

    /// <summary>The languages that can be chosen, English first.</summary>
    IReadOnlyList<LanguageInfo> Languages { get; }

    /// <summary>Raised after the language changed.</summary>
    event EventHandler? LanguageChanged;

    /// <summary>
    /// The text for <paramref name="key"/>; with <paramref name="args"/> it is a composite format string
    /// filled in with the current culture. With no arguments the text is returned as it is.
    /// </summary>
    string Get(string key, params object?[] args);

    /// <summary>Looks the key up without the key-as-text fallback; false when no language has it.</summary>
    bool TryGet(string key, out string value);

    /// <summary>
    /// Makes <paramref name="code"/> the current language and saves it. Returns false, changing nothing,
    /// when the code is not one of <see cref="Languages"/>.
    /// </summary>
    bool SetLanguage(string code);
}

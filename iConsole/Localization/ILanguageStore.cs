namespace iConsole.Localization;

/// <summary>Where the chosen language is kept between runs. The app backs it with its settings.</summary>
public interface ILanguageStore
{
    /// <summary>The saved language code, or null when none was saved.</summary>
    string? Load();

    /// <summary>Saves the language code to start with next time.</summary>
    void Save(string code);
}

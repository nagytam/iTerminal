using iConsole.Localization;
using iTerminal.Core.Data;

namespace iTerminal.Core.Services;

/// <summary>Keeps the language chosen in the language dialog in the settings, so the next run starts in it.</summary>
public sealed class SettingsLanguageStore(ISettingsService settingsService) : ILanguageStore
{
    public string? Load() => settingsService.Get(Settings.Language);

    public void Save(string code) => settingsService.Set(Settings.Language, code);
}

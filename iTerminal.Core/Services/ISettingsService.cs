namespace iTerminal.Core.Services;

public interface ISettingsService
{
    bool IsDirty { get; }

    string? Get(string key);
    bool? GetAsBool(string key, bool? defaultValue = false);
    void Set(string key, string value);
    void Write();
}
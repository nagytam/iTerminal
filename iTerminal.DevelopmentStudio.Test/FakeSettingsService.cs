using iTerminal.Core.Services;

namespace iTerminal.DevelopmentStudio.Test;

/// <summary>Keeps the settings in memory, so a test can open the studio twice on the same settings, as the application does across a restart.</summary>
public sealed class FakeSettingsService : ISettingsService
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public bool IsDirty { get; private set; }

    public string? Get(string key)
    {
        lock (_values) return _values.GetValueOrDefault(key);
    }

    public bool? GetAsBool(string key, bool? defaultValue = false) =>
        bool.TryParse(Get(key), out bool value) ? value : defaultValue;

    public void Set(string key, string value)
    {
        lock (_values) _values[key] = value;
        IsDirty = true;
    }

    public void Write() => IsDirty = false;
}

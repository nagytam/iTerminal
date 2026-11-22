using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Helper;

/// <summary>An in-memory <see cref="ISettingsService"/> for tests; shared by the Core and widget test projects.</summary>
public sealed class MemorySettings : ISettingsService
{
    private readonly Dictionary<string, string> _values = [];
    public bool IsDirty => false;
    public string? Get(string key) => _values.GetValueOrDefault(key);
    public bool? GetAsBool(string key, bool? defaultValue = false) => defaultValue;
    public void Set(string key, string value) => _values[key] = value;
    public void Write()
    {
    }
}

namespace iTerminal.Core.Services;

public sealed class NullSettingsService : ISettingsService
{
    public static readonly NullSettingsService Instance = new();

    private NullSettingsService()
    {
    }

    public bool IsDirty => false;

    public string? Get(string key)
    {
        return null;
    }

    public bool? GetAsBool(string key, bool? defaultValue = false)
    {
        return defaultValue;
    }

    public void Set(string key, string value)
    {
    }

    public void Write()
    {
    }
}

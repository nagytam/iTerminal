namespace iConsole.Toml;

/// <summary>The extensions F3 opens in the TOML viewer.</summary>
public static class TomlFileTypes
{
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".toml" };

    public static bool IsTomlFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));
}

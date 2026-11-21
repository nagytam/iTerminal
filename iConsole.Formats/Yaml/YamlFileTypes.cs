namespace iConsole.Yaml;

/// <summary>The extensions F3 opens in the YAML viewer.</summary>
public static class YamlFileTypes
{
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".yaml", ".yml" };

    public static bool IsYamlFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));
}

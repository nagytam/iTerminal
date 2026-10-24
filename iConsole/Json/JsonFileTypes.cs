namespace iConsole.Json;

/// <summary>The extensions F3 opens in the JSON viewer.</summary>
public static class JsonFileTypes
{
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json" };

    public static bool IsJsonFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));
}

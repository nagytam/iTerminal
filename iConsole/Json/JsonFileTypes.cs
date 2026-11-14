namespace iConsole.Json;

/// <summary>
/// The extensions F3 opens in the JSON viewer. A Jupyter notebook and a GeoJSON document are plain JSON under their own
/// names, so they open here too.
/// </summary>
public static class JsonFileTypes
{
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json", ".ipynb", ".geojson" };

    public static bool IsJsonFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));
}

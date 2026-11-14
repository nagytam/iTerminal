namespace iConsole.Har;

/// <summary>The extensions F3 opens in the HAR viewer. The contents are checked when the file is parsed, not here.</summary>
public static class HarFileTypes
{
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".har" };

    public static bool IsHarFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));
}

namespace iConsole.Xps;

/// <summary>The extensions F3 opens in the XPS viewer. An OpenXPS package is the same format under a different namespace.</summary>
public static class XpsFileTypes
{
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xps", ".oxps" };

    public static bool IsXpsFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));
}

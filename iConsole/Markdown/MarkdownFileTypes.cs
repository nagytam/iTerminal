namespace iConsole.Markdown;

/// <summary>The extensions F3 opens in the Markdown viewer.</summary>
public static class MarkdownFileTypes
{
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown" };

    public static bool IsMarkdownFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));
}

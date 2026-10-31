namespace iConsole.Csv;

public static class CsvFileTypes
{
    /// <summary>The delimited-text endings the CSV viewer opens (a tab-separated file is the same grid with a tab).</summary>
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".csv", ".tsv" };

    public static bool IsCsvFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));
}

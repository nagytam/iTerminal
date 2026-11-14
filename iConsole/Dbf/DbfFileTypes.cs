namespace iConsole.Dbf;

/// <summary>The extensions F3 opens in the dBase table viewer.</summary>
public static class DbfFileTypes
{
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dbf" };

    public static bool IsDbfFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));
}

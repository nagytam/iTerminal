namespace iConsole.Sqlite;

public static class SqliteFileTypes
{
    /// <summary>The endings the SQLite viewer opens. A .db file is not always SQLite, so the header is checked too.</summary>
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".sqlite", ".sqlite3", ".db", ".db3" };

    /// <summary>The first 16 bytes of every SQLite database: "SQLite format 3" and a NUL.</summary>
    public static readonly byte[] Header = "SQLite format 3\0"u8.ToArray();

    public static bool IsSqliteFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));

    /// <summary>True when the bytes begin with the SQLite header. A file shorter than the header, such as an empty one, is not a database.</summary>
    public static bool HasSqliteHeader(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= Header.Length && bytes[..Header.Length].SequenceEqual(Header);

    /// <summary>Reads the first bytes of a local file and checks them. Blocking I/O: call it from a worker thread.</summary>
    public static bool FileHasSqliteHeader(string path)
    {
        byte[] buffer = new byte[Header.Length];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return HasSqliteHeader(buffer.AsSpan(0, read));
    }
}

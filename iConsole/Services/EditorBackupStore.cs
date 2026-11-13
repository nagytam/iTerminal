using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace iConsole.Services;

/// <summary>The unsaved text of one file, as the editor last backed it up.</summary>
public sealed record EditorBackup(DateTime SavedAtUtc, string[] Lines, string LineEnding, string EncodingName);

/// <summary>
/// Keeps a copy of an editor's unsaved text in a folder, so a crash or a closed window does not lose it. One file per
/// document, named by a hash of its address, and the file records that address so a hash clash is never restored into the
/// wrong document. A backup is written atomically (a temporary file, then a move). Reading and writing do disk I/O: call
/// them from a background task.
/// </summary>
public sealed class EditorBackupStore
{
    private readonly string _directory;

    public EditorBackupStore(string directory)
    {
        _directory = directory;
    }

    /// <summary>The folder the backups are kept in.</summary>
    public string Directory => _directory;

    /// <summary>The file a backup of <paramref name="key"/> is kept in.</summary>
    public string PathFor(string key) =>
        Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");

    /// <summary>Writes the backup of <paramref name="key"/>, replacing an earlier one.</summary>
    public void Write(string key, EditorBackup backup)
    {
        System.IO.Directory.CreateDirectory(_directory);
        var path = PathFor(key);
        var temporary = path + ".tmp";
        var stored = new StoredBackup
        {
            Key = key,
            SavedAtUtc = backup.SavedAtUtc,
            LineEnding = backup.LineEnding,
            Encoding = backup.EncodingName,
            Lines = backup.Lines,
        };
        using (var stream = File.Create(temporary))
        {
            JsonSerializer.Serialize(stream, stored);
        }

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The backup of <paramref name="key"/>, or null when there is none or it cannot be read.</summary>
    public EditorBackup? Read(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var stored = JsonSerializer.Deserialize<StoredBackup>(stream);
            if (stored is null || stored.Key != key)
            {
                return null;
            }

            return new EditorBackup(
                DateTime.SpecifyKind(stored.SavedAtUtc, DateTimeKind.Utc),
                stored.Lines ?? [],
                stored.LineEnding ?? "\r\n",
                stored.Encoding ?? Encoding.UTF8.WebName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Removes the backup of <paramref name="key"/>, if there is one.</summary>
    public void Delete(string key)
    {
        try
        {
            File.Delete(PathFor(key));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A backup that cannot be removed is harmless: a restore only uses one newer than the file on disk.
        }
    }

    private sealed class StoredBackup
    {
        public string Key { get; set; } = string.Empty;
        public DateTime SavedAtUtc { get; set; }
        public string LineEnding { get; set; } = "\r\n";
        public string Encoding { get; set; } = string.Empty;
        public string[]? Lines { get; set; }
    }
}

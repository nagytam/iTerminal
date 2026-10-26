namespace iConsole.Controls;

/// <summary>One row of a <see cref="FilePicker"/> listing.</summary>
public sealed record FilePickerEntry(string Name, bool IsFolder, long Size = 0, bool IsHidden = false);

/// <summary>Where a <see cref="FilePicker"/> reads folders from: the local disk, an archive, a remote share.</summary>
public interface IFilePickerSource
{
    /// <summary>The folder to start in when none is given.</summary>
    string DefaultFolder { get; }

    /// <summary>The entries directly in <paramref name="folder"/>, in any order. Throws when the folder cannot be read.</summary>
    Task<IReadOnlyList<FilePickerEntry>> ListAsync(string folder, CancellationToken cancellationToken);

    /// <summary>The folder above <paramref name="folder"/>, or null at the top.</summary>
    string? ParentOf(string folder);

    /// <summary>The path of <paramref name="name"/> inside <paramref name="folder"/>.</summary>
    string Combine(string folder, string name);
}

/// <summary>The local disk.</summary>
public sealed class LocalFilePickerSource : IFilePickerSource
{
    public string DefaultFolder => Environment.CurrentDirectory;

    public Task<IReadOnlyList<FilePickerEntry>> ListAsync(string folder, CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<FilePickerEntry>>(() =>
        {
            var entries = new List<FilePickerEntry>();
            var directory = new DirectoryInfo(folder);
            foreach (FileSystemInfo info in directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool isHidden = (info.Attributes & FileAttributes.Hidden) != 0 || info.Name.StartsWith('.');
                if (info is FileInfo file)
                {
                    entries.Add(new FilePickerEntry(info.Name, false, file.Length, isHidden));
                }
                else
                {
                    entries.Add(new FilePickerEntry(info.Name, true, 0, isHidden));
                }
            }

            return entries;
        }, cancellationToken);
    }

    public string? ParentOf(string folder)
    {
        return Directory.GetParent(Path.TrimEndingDirectorySeparator(folder))?.FullName;
    }

    public string Combine(string folder, string name) => Path.Combine(folder, name);
}

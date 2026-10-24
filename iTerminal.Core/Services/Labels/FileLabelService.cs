using System.Collections.Concurrent;
using iTerminal.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Services.Labels;

public interface IFileLabelService
{
    /// <summary>
    /// The label of a file or folder, read from memory so it is safe on the render thread. Empty until the labels have
    /// been read from the database, which starts on the first call.
    /// </summary>
    FileLabel Get(string path);

    /// <summary>Waits until the labels are read, so <see cref="Get"/> answers for every label that is saved.</summary>
    Task WhenLoadedAsync();

    /// <summary>
    /// Changes the label of each path: <paramref name="change"/> gets what the path has now and returns what it should
    /// have. A label that comes back empty removes the path's row.
    /// </summary>
    Task UpdateAsync(IReadOnlyList<string> paths, Func<FileLabel, FileLabel> change);
}

/// <summary>
/// Keeps the file colour labels and tags in the database, and a copy of them in memory for the file panel to draw from.
/// A label follows the path it was given to: renaming or moving a file leaves its label behind.
/// </summary>
public sealed class FileLabelService(IDbContextFactory<iTerminalDbContext> dbContextFactory) : IFileLabelService
{
    private readonly ConcurrentDictionary<string, FileLabel> _labels = new(StringComparer.Ordinal);
    private readonly object _loadLock = new();
    private Task? _loading;

    public FileLabel Get(string path)
    {
        _ = EnsureLoadedAsync();
        string key;
        try
        {
            key = Key(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return FileLabel.None;
        }

        return _labels.TryGetValue(key, out var label) ? label : FileLabel.None;
    }

    public Task WhenLoadedAsync() => EnsureLoadedAsync();

    public async Task UpdateAsync(IReadOnlyList<string> paths, Func<FileLabel, FileLabel> change)
    {
        // The labels are read first, so a write never lands under a copy of them that is still being read.
        await EnsureLoadedAsync();
        var keys = paths.Select(Key).Distinct(StringComparer.Ordinal).ToList();
        var changes = keys
            .Select(key => (Key: key, Label: change(_labels.TryGetValue(key, out var current) ? current : FileLabel.None)))
            .ToList();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        dbContext.FileLabels.RemoveRange(await dbContext.FileLabels.Where(entry => keys.Contains(entry.Path)).ToListAsync());
        foreach (var (key, label) in changes.Where(item => !item.Label.IsEmpty))
        {
            dbContext.FileLabels.Add(new FileLabelEntry
            {
                Path = key,
                Colour = label.Colour == LabelColour.None ? string.Empty : LabelColours.Name(label.Colour),
                Tags = string.Join(',', label.Tags),
            });
        }

        await dbContext.SaveChangesAsync();

        foreach (var (key, label) in changes)
        {
            if (label.IsEmpty)
            {
                _labels.TryRemove(key, out _);
            }
            else
            {
                _labels[key] = label;
            }
        }
    }

    private Task EnsureLoadedAsync()
    {
        lock (_loadLock)
        {
            if (_loading is null)
            {
                var loading = LoadAsync();
                _loading = loading;

                // A load that failed (the database not ready yet, say) is tried again on the next read or write.
                _ = loading.ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        lock (_loadLock)
                        {
                            if (ReferenceEquals(_loading, loading))
                            {
                                _loading = null;
                            }
                        }
                    }
                }, TaskContinuationOptions.ExecuteSynchronously);
            }

            return _loading;
        }
    }

    private async Task LoadAsync()
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        foreach (var row in await dbContext.FileLabels.AsNoTracking().ToListAsync())
        {
            var tags = row.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _labels[row.Path] = new FileLabel(LabelColours.Parse(row.Colour), tags);
        }
    }

    /// <summary>
    /// The key a path is kept under: its full path without a trailing separator, in lower case on Windows, where the file
    /// system ignores case.
    /// </summary>
    private static string Key(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
    }
}

using iConsole.Data;
using iConsole.Display;
using iConsole.Model;

namespace iConsole.Interfaces;

public interface IContentModel : IScrollableModel, IFileSystem
{
    ConcurrentObservableCollection<IEntry> Entries { get; }
    string? Filter { get; set; }
    IList<IEntry> FilteredEntries { get; }
    long Length { get; }
    string Name { get; }
    Url Url { get; }
    IContentBrowser ContentBrowser { get; set; }
    IEntry? GetFocusedEntry();
    void Refresh(bool forceRefresh=false);
    void ResetNavigation();

    /// <summary>
    /// Puts the cursor and the first visible row back where a previous session left them. Applied
    /// as soon as a listing that can satisfy it exists, which for a folder still being enumerated is
    /// not yet. <paramref name="focusedEntryUrl"/> is what the cursor is restored to when it is
    /// still there; the indexes are the fallback.
    /// </summary>
    void RestoreNavigation(string? focusedEntryUrl, long cursorPositionY, long scrollPositionY);
    void SetUrl(Url url);
    string GetHeader(int width);
    string GetFooter();
    ConxelString FormatItemName(IEntry? entry, int width, bool cursor, ColorScheme colorScheme, VisualState visualState);
    void ReadUrl();
    void Delete(Url url, bool recursive, bool moveToRecycleBin);
    Task RunTerminalCommandAsync(ITerminalModel terminalModel);
}
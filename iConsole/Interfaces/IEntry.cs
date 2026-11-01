using iConsole.Data;
using iConsole.Display;
using System.ComponentModel;

namespace iConsole.Interfaces;

public interface IEntry : INotifyPropertyChanged
{
    string FileSize { get; }
    string Date { get; }
    string Time { get; }
    string DisplayName { get; }
    string Name { get; }
    string FullName { get; }
    Url Url { get; }
    string DirectoryName { get; }
    string? NumberOfFiles { get; set; }
    bool Exists { get; }
    string GetDisplayName(int width);
    string GetDisplayFileSize(int width);
    ConxelString FormatItemName(int width, bool cursor, ColorScheme colorScheme, VisualState visualState);

    /// <summary>
    /// The name alone, for a Brief listing cell (no size or date). Defaults to the full row so an entry
    /// that does not know about Brief still draws.
    /// </summary>
    ConxelString FormatBriefItemName(int width, bool cursor, ColorScheme colorScheme, VisualState visualState) =>
        FormatItemName(width, cursor, colorScheme, visualState);
}

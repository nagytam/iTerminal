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
}

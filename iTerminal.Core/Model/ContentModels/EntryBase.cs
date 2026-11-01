using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace iTerminal.Core.Model.ContentModels;

public abstract record EntryBase : IEntry
{
    protected string fileSize = string.Empty;
    public virtual string FileSize //=> Url.FileSize;
    {
        get { return fileSize; }
        set { fileSize = value; }
    }
    protected string date = string.Empty;
    public virtual string Date
    {
        get { return date; }
        set { date = value; }
    }
    protected string time = string.Empty;
    public virtual string Time 
    {
        get { return time; }
        set { time = value; }
    }
    public virtual string FullName => Url.FullName;
    public virtual string DisplayName => Url.DisplayName;
    public virtual string Name => Url.Name;
    public virtual Url Url { get; protected set; } = null!;
    public virtual string DirectoryName => Url.Directory;
    public virtual bool Exists => Url.Exists;

    private string? _numberOfFiles = null;
    public virtual string? NumberOfFiles 
    { 
        get
        {
            return _numberOfFiles;
        }
        set
        {
            if (_numberOfFiles != value)
            {
                _numberOfFiles = value;
                OnPropertyChanged(nameof(NumberOfFiles));
            }
        }
    }

    public virtual string GetDisplayName(int width)
    {
        if (Url is not null)
        {
            if (Url.IsLink)
            {
                // The name as listed, not the folder's last path segment: for a link the two are
                // the same thing, and the target is what there is to say about it.
                return $"Link: {Url.DisplayName} {LinkArrow} {Url.LinkTarget}".PadRight(width);
            }

            var name = (Url.PathType == PathType.File) ? "File: " : "Folder: ";
            name += GetName(width);
            return name;
        }
        return string.Empty;
    }

    /// <summary>Between a link's name and where it points, in the listing and the info strip.</summary>
    public const string LinkArrow = "→";

    public virtual string GetName(int width)
    {
        switch (Url.PathType)
        {
            case PathType.File:
                {
                    return Url.DisplayName.PadRight(width);
                }
            case PathType.Folder:
                {
                    return DirectoryName?.PadRight(width) ?? string.Empty;
                }
        }
        return string.Empty.PadRight(width);
    }

    public virtual string GetDisplayFileSize(int width)
    {
        var fileSize = (Url.PathType == PathType.File) ? "File size: " : string.Empty;
        fileSize += GetFileSize(width);
        return fileSize;
    }

    public virtual string GetFileSize(int width)
    {
        if (Url.PathType == PathType.File)
        {
            return Url.FileSize.PadRight(width);
        }
        else if (NumberOfFiles != null)
        {
            return $"Files: {NumberOfFiles}".PadRight(width);
        }
        else
        {
            // Not counted yet: the count starts shortly after the cursor stops on the folder.
            return "Files: …".PadRight(width);
        }
    }

    public Color GetForegroundColor(ColorScheme colorScheme, bool cursor, VisualState visualState)
    {
        var color = colorScheme.Foreground;
        if (Url is not null)
        {
            if (Url.Exists)
            {
                if (cursor)
                {
                    color = colorScheme.SelectedFocusedForeground;
                }
                else
                {
                    color = Color.Cyan;
                }
            }
            else
            {
                if (cursor)
                {
                    color = colorScheme.SelectedFocusedForeground;
                }
                else
                {
                    color = colorScheme.Foreground;
                }
            }
        }
        return color;
    }

    public Color GetBackgroundColor(ColorScheme colorScheme, bool cursor, VisualState visualState)
    {
        var color = colorScheme.Background;
        if (cursor)
        {
            if (visualState == VisualState.Focused)
            {
                color = Color.Yellow;
            }
            else
            {
                color = Color.DarkYellow;
            }
        }
        return color;
    }

    internal const int DateLength = 10;
    internal const int TimeLength = 5;
    internal const int SizeLength = 6;

    /// <summary>
    /// How a panel row of <paramref name="width"/> is divided. A narrow panel gives the name its room
    /// back: time goes first, then date. At 60 columns the name column was 4 wide ("alph}"), so no
    /// file could be told from another.
    /// </summary>
    internal static (int NameLength, bool ShowDate, bool ShowTime) ColumnLayout(int width)
    {
        const int minNameLength = 14;
        var showTime = width - 2 - 1 - SizeLength - 1 - DateLength - 1 - TimeLength >= minNameLength;
        var showDate = showTime || width - 2 - 1 - SizeLength - 1 - DateLength >= minNameLength;
        var nameLength = Math.Max(0, width - 2 - 1 - SizeLength
            - (showDate ? 1 + DateLength : 0) - (showTime ? 1 + TimeLength : 0));
        return (nameLength, showDate, showTime);
    }

    public virtual ConxelString FormatItemName(int width, bool cursor, ColorScheme colorScheme, VisualState visualState)
    {
        const int dateLength = DateLength;
        const int timeLength = TimeLength;
        const int sizeLength = SizeLength;
        var (nameLength, showDate, showTime) = ColumnLayout(width);

        ConxelString conxelString = new();
        if (Url is not null)
        {
            var foregroundColor = GetForegroundColor(colorScheme, cursor, visualState);
            var backgroundColor = GetBackgroundColor(colorScheme, cursor, visualState);
            var isItalic = Url.PathType == PathType.Folder;
            // A link says where it leads, in the name column: that is the one thing that tells it
            // from the file or folder it stands for, and what F8 on it will — and will not — delete.
            var displayName = Url.IsLink ? $"{Url.DisplayName} {LinkArrow} {Url.LinkTarget}" : Url.DisplayName;

            AppendRightPadded(conxelString, displayName, nameLength, foregroundColor, backgroundColor, isItalic);
            conxelString.Append(displayName.Length <= nameLength ? Border.VerticalSingle : "}", foregroundColor, backgroundColor, isItalic: isItalic);
            AppendLeftPadded(conxelString, FileSize, sizeLength, foregroundColor, backgroundColor, isItalic);
            // Padded to their columns: a row that has no date (the '..' of a tool-backed drive) must
            // still paint every cell, or the previous frame's date stays on screen beside it.
            if (showDate)
            {
                conxelString.Append(Border.VerticalSingle, foregroundColor, backgroundColor, isItalic: isItalic);
                AppendRightPadded(conxelString, Date, dateLength, foregroundColor, backgroundColor, isItalic);
            }

            if (showTime)
            {
                conxelString.Append(Border.VerticalSingle, foregroundColor, backgroundColor, isItalic: isItalic);
                AppendRightPadded(conxelString, Time, timeLength, foregroundColor, backgroundColor, isItalic);
            }
        }
        return conxelString;
    }

    /// <summary>Width of the attributes column in a Long listing row.</summary>
    internal const int AttributeLength = 9;

    /// <summary>
    /// The Long listing row: name, attributes, date and time, with no size column. Narrower than the minimum for
    /// all four columns, it falls back to the full row so nothing is cut mid-column.
    /// </summary>
    public virtual ConxelString FormatLongItemName(int width, bool cursor, ColorScheme colorScheme, VisualState visualState)
    {
        var fixedWidth = AttributeLength + 1 + DateLength + 1 + TimeLength + 1;
        if (Url is null || width < fixedWidth + 8)
        {
            return FormatItemName(width, cursor, colorScheme, visualState);
        }

        ConxelString conxelString = new();
        var foregroundColor = GetForegroundColor(colorScheme, cursor, visualState);
        var backgroundColor = GetBackgroundColor(colorScheme, cursor, visualState);
        var isItalic = Url.PathType == PathType.Folder;
        var displayName = Url.IsLink ? $"{Url.DisplayName} {LinkArrow} {Url.LinkTarget}" : Url.DisplayName;
        var nameLength = width - fixedWidth;

        AppendRightPadded(conxelString, displayName, nameLength, foregroundColor, backgroundColor, isItalic);
        conxelString.Append(Border.VerticalSingle, foregroundColor, backgroundColor, isItalic: isItalic);
        AppendRightPadded(conxelString, FormatAttributes(Url), AttributeLength, foregroundColor, backgroundColor, isItalic);
        conxelString.Append(Border.VerticalSingle, foregroundColor, backgroundColor, isItalic: isItalic);
        AppendRightPadded(conxelString, Date, DateLength, foregroundColor, backgroundColor, isItalic);
        conxelString.Append(Border.VerticalSingle, foregroundColor, backgroundColor, isItalic: isItalic);
        AppendRightPadded(conxelString, Time, TimeLength, foregroundColor, backgroundColor, isItalic);
        return conxelString;
    }

    /// <summary>
    /// The attributes of an entry in nine characters: <c>rwxrwxrwx</c> on Unix, <c>RHSA</c> letters on Windows
    /// (dashes where an attribute is clear), blank when the Url carries no attributes (made from a path string).
    /// </summary>
    public static string FormatAttributes(Url url)
    {
        if (url.UnixMode is { } mode)
        {
            const string letters = "rwxrwxrwx";
            var bits = (int)mode;
            var text = new System.Text.StringBuilder(AttributeLength);
            for (var i = 0; i < AttributeLength; i++)
            {
                text.Append((bits & (1 << (AttributeLength - 1 - i))) != 0 ? letters[i] : '-');
            }

            return text.ToString();
        }

        if (url.Attributes is { } attributes)
        {
            var text = (attributes & System.IO.FileAttributes.ReadOnly) != 0 ? "R" : "-";
            text += (attributes & System.IO.FileAttributes.Hidden) != 0 ? "H" : "-";
            text += (attributes & System.IO.FileAttributes.System) != 0 ? "S" : "-";
            text += (attributes & System.IO.FileAttributes.Archive) != 0 ? "A" : "-";
            return text.PadRight(AttributeLength);
        }

        return new string(' ', AttributeLength);
    }

    /// <summary>
    /// The name alone for a Brief listing cell: no size, date or time column. <paramref name="width"/> is the
    /// name width; the caller draws the column separator.
    /// </summary>
    public virtual ConxelString FormatBriefItemName(int width, bool cursor, ColorScheme colorScheme, VisualState visualState)
    {
        ConxelString conxelString = new();
        if (Url is not null)
        {
            var foregroundColor = GetForegroundColor(colorScheme, cursor, visualState);
            var backgroundColor = GetBackgroundColor(colorScheme, cursor, visualState);
            var isItalic = Url.PathType == PathType.Folder;
            var displayName = Url.IsLink ? $"{Url.DisplayName} {LinkArrow} {Url.LinkTarget}" : Url.DisplayName;
            AppendRightPadded(conxelString, displayName, width, foregroundColor, backgroundColor, isItalic);
        }
        return conxelString;
    }

    private static void AppendRightPadded(
        ConxelString conxelString,
        string value,
        int width,
        Color foregroundColor,
        Color backgroundColor,
        bool isItalic)
    {
        var contentLength = Math.Min(value.Length, width);
        for (var i = 0; i < contentLength; i++)
        {
            conxelString.Append(value[i], foregroundColor, backgroundColor, isItalic: isItalic);
        }

        for (var i = contentLength; i < width; i++)
        {
            conxelString.Append(' ', foregroundColor, backgroundColor, isItalic: isItalic);
        }
    }

    private static void AppendLeftPadded(
        ConxelString conxelString,
        string value,
        int width,
        Color foregroundColor,
        Color backgroundColor,
        bool isItalic)
    {
        var contentLength = Math.Min(value.Length, width);
        for (var i = contentLength; i < width; i++)
        {
            conxelString.Append(' ', foregroundColor, backgroundColor, isItalic: isItalic);
        }

        for (var i = 0; i < contentLength; i++)
        {
            conxelString.Append(value[i], foregroundColor, backgroundColor, isItalic: isItalic);
        }
    }

    public virtual void HighlightFolder(ConxelString formatting)
    {
        switch (Url.PathType)
        {
            case PathType.Folder:
                {
                    for (int i = 0; i < formatting.Count; i++)
                    {
                        var cell = formatting.String[i];
                        cell.IsItalic = true;
                        formatting.String[i] = cell;
                    }
                    break;
                }
            case PathType.File:
                {
                    break;
                }
        }
    }

    public override int GetHashCode()
    {
        var s = FullName + Date + Time + FileSize;
        return s.GetHashCode();
    }

    public virtual bool Equals(EntryBase? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return FullName == other.FullName
            && Date == other.Date
            && Time == other.Time
            && FileSize == other.FileSize;
    }

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    #endregion
}

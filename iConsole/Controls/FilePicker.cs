using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>
/// Browses folders and picks a file, or a folder with <see cref="PickFolders"/>. The first row shows the current
/// folder, the rest list a <c>..</c> row, the sub-folders and then the files; Up, Down, PageUp, PageDown, Home and End
/// move the cursor, Enter opens a folder or chooses a file, Backspace goes up one folder, and a click moves the cursor
/// (a second click on the same row acts as Enter). Folders are read on a background task through an
/// <see cref="IFilePickerSource"/>, and a listing that arrives after the user moved on is dropped.
/// </summary>
public class FilePicker : Control
{
    private const string ParentName = "..";
    private const int WheelRows = 3;

    private readonly IFilePickerSource _source;
    private readonly Lock _gate = new();
    private List<FilePickerEntry> _rows = [];
    private CancellationTokenSource? _loading;
    private int _loadVersion;
    private string _folder;
    private string? _error;
    private bool _isLoading;
    private int _cursor;
    private int _top;
    private string _filter = string.Empty;
    private bool _showHidden;
    private bool _pickFolders;

    public FilePicker(IFilePickerSource source, IThemeService themeService) : base(themeService)
    {
        _source = source;
        _folder = source.DefaultFolder;
        CanFocus = true;
        ViewSize = new Size(40, 12);
    }

    /// <summary>Raised with the full path when the user chooses a file (or a folder when <see cref="PickFolders"/> is set).</summary>
    public event Action<string>? PathChosen;

    /// <summary>Raised when a listing has been shown or has failed; tests and callers wait on it.</summary>
    public event Action? Loaded;

    /// <summary>The folder being shown.</summary>
    public string FolderPath
    {
        get { lock (_gate) { return _folder; } }
    }

    /// <summary>
    /// File name patterns to show, separated by <c>;</c>, such as <c>*.txt;*.md</c>. Empty shows every file. Folders always show.
    /// </summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (_filter != (value ?? string.Empty))
            {
                _filter = value ?? string.Empty;
                RebuildRows();
            }
        }
    }

    public bool ShowHidden
    {
        get => _showHidden;
        set
        {
            if (_showHidden != value)
            {
                _showHidden = value;
                RebuildRows();
            }
        }
    }

    /// <summary>When set the picker chooses folders: Enter on a folder chooses it and files are not listed.</summary>
    public bool PickFolders
    {
        get => _pickFolders;
        set
        {
            if (_pickFolders != value)
            {
                _pickFolders = value;
                RebuildRows();
            }
        }
    }

    /// <summary>The message from the last listing that failed, or null.</summary>
    public string? Error => _error;

    /// <summary>The rows now shown, the <c>..</c> row first when the folder has a parent.</summary>
    public IReadOnlyList<FilePickerEntry> Rows
    {
        get { lock (_gate) { return [.. _rows]; } }
    }

    /// <summary>The row under the cursor, or null for an empty list.</summary>
    public FilePickerEntry? CursorEntry
    {
        get { lock (_gate) { return _cursor >= 0 && _cursor < _rows.Count ? _rows[_cursor] : null; } }
    }

    /// <summary>The path of the row under the cursor, or the shown folder when <see cref="PickFolders"/> is set and the cursor is on <c>..</c>.</summary>
    public string? CursorPath
    {
        get
        {
            FilePickerEntry? entry = CursorEntry;
            if (entry is null)
            {
                return null;
            }

            return entry.Name == ParentName ? _source.ParentOf(FolderPath) : _source.Combine(FolderPath, entry.Name);
        }
    }

    private List<FilePickerEntry> _all = [];

    /// <summary>Shows <paramref name="folder"/>. The returned task ends when the listing has been applied or dropped.</summary>
    public async Task NavigateToAsync(string folder)
    {
        CancellationTokenSource source = new();
        int version;
        lock (_gate)
        {
            _loading?.Cancel();
            _loading = source;
            version = ++_loadVersion;
            _isLoading = true;
        }

        StateHasChanged();
        try
        {
            IReadOnlyList<FilePickerEntry> entries = await _source.ListAsync(folder, source.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (version != _loadVersion)
                {
                    return;
                }

                _folder = folder;
                _all = [.. entries];
                _error = null;
                _isLoading = false;
            }

            RebuildRows(resetCursor: true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (version != _loadVersion)
                {
                    return;
                }

                _error = exception.Message;
                _isLoading = false;
            }

            StateHasChanged();
        }

        Loaded?.Invoke();
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        await NavigateToAsync(FolderPath);
    }

    private void RebuildRows(bool resetCursor = false)
    {
        lock (_gate)
        {
            string[] patterns = _filter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            IEnumerable<FilePickerEntry> shown = _all
                .Where(e => _showHidden || !e.IsHidden)
                .Where(e => e.IsFolder || (!_pickFolders && (patterns.Length == 0 || patterns.Any(p => Matches(p, e.Name)))))
                .OrderByDescending(e => e.IsFolder)
                .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase);
            var rows = new List<FilePickerEntry>();
            if (_source.ParentOf(_folder) is not null)
            {
                rows.Add(new FilePickerEntry(ParentName, true));
            }

            rows.AddRange(shown);
            string? keep = resetCursor ? null : CursorName();
            _rows = rows;
            _cursor = keep is null ? 0 : Math.Max(0, rows.FindIndex(r => r.Name == keep));
            _top = Math.Min(_top, Math.Max(0, rows.Count - 1));
            if (resetCursor)
            {
                _top = 0;
            }

            KeepCursorVisible();
        }

        StateHasChanged();
    }

    private string? CursorName() => _cursor >= 0 && _cursor < _rows.Count ? _rows[_cursor].Name : null;

    /// <summary>True when <paramref name="name"/> matches <paramref name="pattern"/>, where <c>*</c> is any run and <c>?</c> any one character.</summary>
    public static bool Matches(string pattern, string name)
    {
        int p = 0;
        int n = 0;
        int star = -1;
        int mark = 0;
        while (n < name.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(name[n])))
            {
                p++;
                n++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = n;
            }
            else if (star >= 0)
            {
                p = star + 1;
                n = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    private int ListHeight => Math.Max(1, (int)ViewSize.Height - 1);

    private void KeepCursorVisible()
    {
        if (_cursor < _top)
        {
            _top = _cursor;
        }
        else if (_cursor >= _top + ListHeight)
        {
            _top = _cursor - ListHeight + 1;
        }
    }

    private void MoveCursor(int target)
    {
        lock (_gate)
        {
            if (_rows.Count == 0)
            {
                return;
            }

            _cursor = Math.Clamp(target, 0, _rows.Count - 1);
            KeepCursorVisible();
        }

        StateHasChanged();
    }

    /// <summary>Acts on the cursor row as Enter does: opens a folder, goes up on <c>..</c>, or chooses a file.</summary>
    public async Task ActivateAsync()
    {
        FilePickerEntry? entry = CursorEntry;
        if (entry is null)
        {
            return;
        }

        string? path = CursorPath;
        if (path is null)
        {
            return;
        }

        if (entry.IsFolder)
        {
            if (_pickFolders && entry.Name != ParentName)
            {
                PathChosen?.Invoke(path);
                return;
            }

            await NavigateToAsync(path);
        }
        else
        {
            PathChosen?.Invoke(path);
        }
    }

    /// <summary>Goes up one folder; does nothing at the top.</summary>
    public async Task GoUpAsync()
    {
        string? parent = _source.ParentOf(FolderPath);
        if (parent is not null)
        {
            await NavigateToAsync(parent);
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled)
        {
            yield break;
        }

        yield return ("Up/Down", "Move the cursor");
        yield return ("PageUp/PageDown", "Move the cursor by a page");
        yield return ("Home/End", "First or last row");
        yield return ("Enter", _pickFolders ? "Open a folder, or choose it on a second Enter" : "Open a folder or choose a file");
        yield return ("Backspace", "Go up one folder");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled || !IsFocused || !IsEnabled || keyEvent.Alt || keyEvent.Control)
        {
            return isHandled;
        }

        int cursor;
        lock (_gate)
        {
            cursor = _cursor;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.UpArrow:
                MoveCursor(cursor - 1);
                return true;
            case ConsoleKey.DownArrow:
                MoveCursor(cursor + 1);
                return true;
            case ConsoleKey.PageUp:
                MoveCursor(cursor - ListHeight);
                return true;
            case ConsoleKey.PageDown:
                MoveCursor(cursor + ListHeight);
                return true;
            case ConsoleKey.Home:
                MoveCursor(0);
                return true;
            case ConsoleKey.End:
                MoveCursor(int.MaxValue);
                return true;
            case ConsoleKey.Enter:
                await ActivateAsync();
                return true;
            case ConsoleKey.Backspace:
                await GoUpAsync();
                return true;
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        long x = (long)mouseEvent.Position.X - (long)AbsolutePosition.X;
        long y = (long)mouseEvent.Position.Y - (long)AbsolutePosition.Y;
        if (x < 0 || x >= ViewSize.Width || y < 0 || y >= ViewSize.Height || !IsEnabled)
        {
            return isHandled;
        }

        if (mouseEvent.WheelDelta != 0)
        {
            int cursor;
            lock (_gate)
            {
                cursor = _cursor;
            }

            MoveCursor(cursor + (mouseEvent.WheelDelta > 0 ? -WheelRows : WheelRows));
            return true;
        }

        if (mouseEvent.LeftClicked && !mouseEvent.IsRelease && y >= 1)
        {
            int row;
            int cursor;
            lock (_gate)
            {
                row = _top + (int)y - 1;
                cursor = _cursor;
            }

            if (row < Rows.Count)
            {
                SetFocus();
                MoveCursor(row);
                if (row == cursor)
                {
                    await ActivateAsync();
                }

                return true;
            }
        }

        return isHandled;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        int height = (int)ViewSize.Height;
        List<FilePickerEntry> rows;
        int top;
        int cursor;
        string folder;
        string? error;
        bool isLoading;
        lock (_gate)
        {
            rows = [.. _rows];
            top = _top;
            cursor = _cursor;
            folder = _folder;
            error = _error;
            isLoading = _isLoading;
        }

        string header = isLoading ? folder + " ..." : folder;
        if (header.Length > width)
        {
            header = "…" + header[^(width - 1)..];
        }

        WriteLine(buffer, 0, header, colorScheme.Foreground, colorScheme.Background, width);
        for (int line = 1; line < height; line++)
        {
            int index = top + line - 1;
            string text = string.Empty;
            Color foreground = colorScheme.Foreground;
            Color background = colorScheme.Background;
            if (index < rows.Count)
            {
                FilePickerEntry entry = rows[index];
                string size = entry.IsFolder ? string.Empty : entry.Size.ToFileSize();
                string name = entry.IsFolder ? entry.Name + "/" : entry.Name;
                int nameWidth = Math.Max(1, width - size.Length - 2);
                text = " " + name.Cut(nameWidth).PadRight(nameWidth) + size;
                if (index == cursor)
                {
                    bool focused = IsFocused && IsEnabled;
                    foreground = focused ? colorScheme.SelectedFocusedForeground : colorScheme.SelectedForeground;
                    background = focused ? colorScheme.SelectedFocusedBackground : colorScheme.SelectedBackground;
                }
                else if (!IsEnabled)
                {
                    foreground = colorScheme.DisabledForeground;
                    background = colorScheme.DisabledBackground;
                }
            }
            else if (line == 1 && rows.Count == 0)
            {
                text = " " + (error ?? (isLoading ? "Loading..." : "No files"));
                foreground = error is null ? colorScheme.DisabledForeground : colorScheme.Error;
            }

            WriteLine(buffer, line, text, foreground, background, width);
        }
    }

    private void WriteLine(IBuffer buffer, int line, string text, Color foreground, Color background, int width)
    {
        var conxelString = new ConxelString(text.Cut(width), foreground, background);
        conxelString.Expand(width, foreground, background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + line), new Size(width, 1), conxelString);
    }

    protected override void OnBeforeClose()
    {
        lock (_gate)
        {
            _loading?.Cancel();
            _loadVersion++;
        }

        base.OnBeforeClose();
    }
}

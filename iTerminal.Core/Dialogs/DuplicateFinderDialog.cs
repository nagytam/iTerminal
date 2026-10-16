using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Duplicates;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Ctrl+Shift+F7: files with identical content under a folder, grouped, largest waste first. Space
/// marks a copy, <c>a</c> marks every copy but the oldest in each group (<c>n</c>: but the newest),
/// <c>u</c> clears the marks, Delete removes the marked files — to the recycle bin where there is one,
/// after a y/n — and <c>g</c> takes the panel to the folder of the file under the cursor.
///
/// <para><b>One copy of a group is always kept.</b> Marking the last unmarked copy is refused, and the
/// bulk marks leave one standing: a duplicate finder that can delete every copy of a file is a file
/// deleter.</para>
///
/// <para>The scan (<see cref="IDuplicateFinderService"/>) runs on a background task and the dialog says
/// which stage it is in; closing the dialog cancels it. Deleting runs on a task too, and the groups
/// are redrawn without what went — a group with a single copy left is no longer a duplicate and
/// disappears. Local folders only, like the disk-usage view.</para>
/// </summary>
public class DuplicateFinderDialog : Dialog
{
    private const int DateWidth = 16;

    private readonly IDuplicateFinderService _service;
    private readonly IContentModelService? _contentModelService;
    private readonly IFileSystem? _fileSystem;
    private readonly InfoLines _lines;
    private readonly HashSet<string> _marked = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private DuplicateScan? _scan;
    private List<DuplicateGroup> _groups = [];
    private List<Row> _rows = [];
    private List<int> _fileRows = [];
    private string? _panelName;
    private int _cursor;      // index into _fileRows
    private int _top;         // index into _rows
    private bool _loaded;
    private bool _confirming;
    private volatile string _status = string.Empty;
    private volatile bool _statusIsError;
    private int _deleting;
    private readonly Lock _sync = new();

    /// <summary>One line of the list: a group's heading, or one of its files.</summary>
    private readonly record struct Row(int Group, DuplicateFile? File);

    public DuplicateFinderDialog(
        IDuplicateFinderService service,
        IThemeService themeService,
        IContentModelService? contentModelService = null,
        IFileSystemService? fileSystem = null)
        : base(nameof(DuplicateFinderDialog), themeService)
    {
        _service = service;
        _contentModelService = contentModelService;
        _fileSystem = fileSystem;
        Title = "Duplicate files";
        _lines = new InfoLines(themeService);
        AddControl(_lines);
    }

    /// <summary>The scan behind the dialog, for a test to wait on.</summary>
    public DuplicateScan? Scan => _scan;

    /// <summary>The duplicate groups now listed (what is left after deletions).</summary>
    public IReadOnlyList<DuplicateGroup> Groups => _groups;

    public IReadOnlyCollection<string> MarkedPaths => _marked;

    public string Status => _status;

    /// <summary>The file under the cursor, or null while scanning or with nothing listed.</summary>
    public DuplicateFile? Selected => _cursor >= 0 && _cursor < _fileRows.Count ? _rows[_fileRows[_cursor]].File : null;

    /// <summary>The deletion started last — nothing waits on it; it exists so a test can.</summary>
    public Task DeleteTask { get; private set; } = Task.CompletedTask;

    /// <summary>True while a y/n for the deletion is on screen.</summary>
    public bool IsConfirmingDelete => _confirming;

    /// <summary>Sets the folder to search and the panel <c>g</c> navigates; call before showing the dialog.</summary>
    public void Load(string folderPath, string? panelName)
    {
        _panelName = panelName;
        StartScan(folderPath);
    }

    private void StartScan(string path)
    {
        _scan?.Cancel();
        _scan = _service.Start(path);
        _groups = [];
        _marked.Clear();
        _loaded = false;
        _confirming = false;
        _cursor = 0;
        _top = 0;
        BuildRows();
        SetStatus(string.Empty, isError: false);
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            var width = (int)Math.Min(Math.Max(60, Parent.ViewSize.Width - 6), 150);
            var height = (int)Math.Max(12, Parent.ViewSize.Height - 4);
            CenterInParent(new Size(width, height));
        }

        _lines.Position = new Position(2, 1);
        _lines.ViewSize = new Size(Math.Max(1, ViewSize.Width - 4), Math.Max(1, ViewSize.Height - 3));
        base.OnInitializeLayout();
        UpdateLines();
    }

    public override void OnIdle()
    {
        base.OnIdle();
        if (_scan is null)
        {
            return;
        }

        if (!_scan.IsComplete)
        {
            UpdateLines();
        }
        else if (!_loaded)
        {
            _loaded = true;
            _groups = [.. _scan.Groups];
            _cursor = 0;
            _top = 0;
            BuildRows();
            UpdateLines();
        }
    }

    private int VisibleRows => Math.Max(1, (int)_lines.ViewSize.Height - 4);

    /// <summary>Lays the groups out as rows: a heading, then its files; remembers which rows are files, which is what the cursor walks.</summary>
    private void BuildRows()
    {
        var rows = new List<Row>();
        var fileRows = new List<int>();
        for (var g = 0; g < _groups.Count; g++)
        {
            rows.Add(new Row(g, null));
            foreach (var file in _groups[g].Files)
            {
                fileRows.Add(rows.Count);
                rows.Add(new Row(g, file));
            }
        }

        _rows = rows;
        _fileRows = fileRows;
        _cursor = Math.Clamp(_cursor, 0, Math.Max(0, fileRows.Count - 1));
    }

    private void SetStatus(string status, bool isError)
    {
        _status = status;
        _statusIsError = isError;
        UpdateLines();
    }

    private long MarkedBytes() => _groups.Sum(g => g.Size * g.Files.Count(f => _marked.Contains(f.Path)));

    private void UpdateLines()
    {
        // The deletion task rewrites the groups and the marks; a repaint must not read them half way.
        lock (_sync)
        {
            BuildLines();
        }
    }

    private void BuildLines()
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        var width = (int)Math.Max(20, _lines.ViewSize.Width);
        var height = (int)Math.Max(1, _lines.ViewSize.Height);
        var lines = new List<InfoLines.Line>();

        void Status()
        {
            while (lines.Count < height - 1)
            {
                lines.Add(default);
            }

            lines.Add(new(_status, _statusIsError ? colorScheme.Error : colorScheme.Foreground));
            _lines.Lines = lines;
            StateHasChanged();
        }

        if (_scan is null)
        {
            lines.Add(new("Nothing to search.", Color.Yellow));
            Status();
            return;
        }

        if (!_scan.IsComplete)
        {
            lines.Add(new($"Looking for duplicates in {_scan.Path}", colorScheme.Foreground));
            lines.Add(new(_scan.Phase switch
            {
                DuplicateScanPhase.Listing => $"Listing files: {_scan.FilesScanned:N0} so far",
                DuplicateScanPhase.Sampling => "Comparing the start of files that are the same size…",
                _ => $"Reading files that still look alike: {_scan.BytesHashed.ToFileSize()} of {_scan.BytesToHash.ToFileSize()}",
            }, Color.Yellow));
            lines.Add(new(_scan.Current, Color.Cyan));
            lines.Add(default);
            lines.Add(new("Esc stops and closes.", colorScheme.Foreground));
            Status();
            return;
        }

        if (_scan.WasCancelled)
        {
            lines.Add(new("Cancelled.", Color.Yellow));
            Status();
            return;
        }

        lines.Add(new(_scan.Path, Color.Cyan));
        if (_groups.Count == 0)
        {
            lines.Add(new($"No duplicate files among the {_scan.FilesScanned:N0} files looked at.", colorScheme.Foreground));
            Status();
            return;
        }

        var files = _groups.Sum(g => g.Files.Count);
        lines.Add(new(
            $"{_groups.Count:N0} group{(_groups.Count == 1 ? string.Empty : "s")}, {files:N0} files — {_groups.Sum(g => g.Wasted).ToFileSize()} could be freed"
            + (_marked.Count > 0 ? $"   |   {_marked.Count:N0} marked, {MarkedBytes().ToFileSize()}" : string.Empty),
            colorScheme.Foreground));
        lines.Add(default);

        var visible = VisibleRows;
        var cursorRow = _fileRows.Count == 0 ? 0 : _fileRows[Math.Min(_cursor, _fileRows.Count - 1)];
        if (cursorRow < _top)
        {
            // Show the group's heading with its first file when the cursor moves up onto it.
            _top = cursorRow > 0 && _rows[cursorRow - 1].File is null ? cursorRow - 1 : cursorRow;
        }
        else if (cursorRow >= _top + visible)
        {
            _top = cursorRow - visible + 1;
        }

        var pathWidth = Math.Max(10, width - (2 + 4 + 2 + DateWidth));
        for (var i = _top; i < _rows.Count && i < _top + visible; i++)
        {
            var row = _rows[i];
            if (row.File is null)
            {
                var group = _groups[row.Group];
                lines.Add(new(
                    $"{group.Files.Count} × {group.Size.ToFileSize()}   {group.Wasted.ToFileSize()} wasted",
                    Color.Cyan));
                continue;
            }

            var isCursor = _fileRows.Count > 0 && i == _fileRows[Math.Min(_cursor, _fileRows.Count - 1)];
            var mark = _marked.Contains(row.File.Path) ? "x" : " ";
            var text = $"{(isCursor ? "▶" : " ")} [{mark}] {row.File.Path.Cut(pathWidth).ExpandRight(pathWidth)}  {row.File.Modified:yyyy-MM-dd HH:mm}";
            Color? color = isCursor ? Color.Yellow : _marked.Contains(row.File.Path) ? Color.Red : null;
            lines.Add(new(text, color));
        }

        Status();
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_confirming)
        {
            _confirming = false;
            if (keyEvent.ConsoleKey == ConsoleKey.Y && !keyEvent.Alt && !keyEvent.Control)
            {
                StartDelete();
            }
            else
            {
                SetStatus("Nothing was deleted.", isError: false);
            }

            return true;
        }

        if (!keyEvent.Alt && !keyEvent.Control && _scan is { IsComplete: true } && _loaded && Interlocked.CompareExchange(ref _deleting, 0, 0) == 0)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.UpArrow:
                    MoveCursor(-1);
                    return true;
                case ConsoleKey.DownArrow:
                    MoveCursor(1);
                    return true;
                case ConsoleKey.PageUp:
                    MoveCursor(-VisibleRows);
                    return true;
                case ConsoleKey.PageDown:
                    MoveCursor(VisibleRows);
                    return true;
                case ConsoleKey.Home:
                    MoveCursor(-_fileRows.Count);
                    return true;
                case ConsoleKey.End:
                    MoveCursor(_fileRows.Count);
                    return true;
                case ConsoleKey.Spacebar:
                case ConsoleKey.Insert:
                    ToggleMark();
                    MoveCursor(1);
                    return true;
                case ConsoleKey.A:
                    MarkAllButOne(keepOldest: true);
                    return true;
                case ConsoleKey.N:
                    MarkAllButOne(keepOldest: false);
                    return true;
                case ConsoleKey.U:
                    ClearMarks();
                    return true;
                case ConsoleKey.Delete:
                case ConsoleKey.F8:
                    RequestDelete();
                    return true;
                case ConsoleKey.R:
                    StartScan(_scan.Path);
                    return true;
                case ConsoleKey.G:
                    await GoToPanelAsync();
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private void MoveCursor(int step)
    {
        _cursor = Math.Clamp(_cursor + step, 0, Math.Max(0, _fileRows.Count - 1));
        UpdateLines();
    }

    /// <summary>Marks or unmarks the file under the cursor — refusing to mark the last copy of its group that is not marked.</summary>
    public void ToggleMark()
    {
        if (Selected is not { } file)
        {
            return;
        }

        if (_marked.Remove(file.Path))
        {
            UpdateLines();
            return;
        }

        var group = _groups.First(g => g.Files.Contains(file));
        if (group.Files.Count(f => !_marked.Contains(f.Path)) <= 1)
        {
            SetStatus("Keep at least one copy of each file — this is the last one not marked.", isError: true);
            return;
        }

        _marked.Add(file.Path);
        SetStatus(string.Empty, isError: false);
    }

    /// <summary>Marks, in every group, all copies but the oldest (or the newest) — the earliest is usually the original.</summary>
    public void MarkAllButOne(bool keepOldest)
    {
        _marked.Clear();
        foreach (var group in _groups)
        {
            var keep = keepOldest
                ? group.Files.OrderBy(f => f.Modified).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).First()
                : group.Files.OrderByDescending(f => f.Modified).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).First();
            foreach (var file in group.Files.Where(f => f != keep))
            {
                _marked.Add(file.Path);
            }
        }

        SetStatus($"Marked {_marked.Count:N0} files, keeping the {(keepOldest ? "oldest" : "newest")} of each group. Delete removes them.", isError: false);
    }

    public void ClearMarks()
    {
        _marked.Clear();
        SetStatus(string.Empty, isError: false);
    }

    /// <summary>Delete: asks first — the marked files, how much they come to, and where they go.</summary>
    public void RequestDelete()
    {
        if (_marked.Count == 0)
        {
            SetStatus("Nothing is marked: Space marks a file, a marks all but the oldest copy of each.", isError: true);
            return;
        }

        if (_fileSystem is null)
        {
            SetStatus("Deleting is not available here.", isError: true);
            return;
        }

        _confirming = true;
        var where = OperatingSystem.IsWindows() || OperatingSystem.IsLinux() ? "to the recycle bin" : "for good";
        SetStatus($"Delete {_marked.Count:N0} files ({MarkedBytes().ToFileSize()}) {where}? y/n", isError: true);
    }

    private void StartDelete()
    {
        var fileSystem = _fileSystem;
        if (fileSystem is null || Interlocked.Exchange(ref _deleting, 1) == 1)
        {
            return;
        }

        var paths = _marked.ToList();
        SetStatus($"Deleting {paths.Count:N0} files…", isError: false);
        DeleteTask = Task.Run(() =>
        {
            var removed = new HashSet<string>(_marked.Comparer);
            var failures = new List<string>();
            foreach (var path in paths)
            {
                try
                {
                    if (fileSystem.DeleteFile(new Url(path), moveToRecycleBin: true))
                    {
                        removed.Add(path);
                    }
                    else
                    {
                        failures.Add($"{Path.GetFileName(path)}: could not be deleted");
                    }
                }
                catch (Exception exception)
                {
                    failures.Add($"{Path.GetFileName(path)}: {exception.Message}");
                }
            }

            lock (_sync)
            {
                _groups = [.. _groups.Select(g => g.Without(removed)).OfType<DuplicateGroup>()];
                _marked.ExceptWith(removed);
                BuildRows();
            }

            Interlocked.Exchange(ref _deleting, 0);
            var text = $"Deleted {removed.Count:N0} file{(removed.Count == 1 ? string.Empty : "s")}.";
            SetStatus(failures.Count == 0 ? text : $"{text} {failures.Count} failed: {failures[0]}", isError: failures.Count > 0);
        });
    }

    /// <summary>Points the panel the dialog was opened over at the folder of the file under the cursor, and closes.</summary>
    public async Task GoToPanelAsync()
    {
        if (Selected is not { } file || _contentModelService is null || file.Folder.Length == 0)
        {
            return;
        }

        var url = new Url(Container.FILE + file.Folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        Close();
        if (_panelName is not null)
        {
            await _contentModelService.SetUrlAsync(_panelName, url);
        }
        else
        {
            await _contentModelService.SetUrlAsync(url);
        }
    }

    public override void Close()
    {
        _scan?.Cancel();
        base.Close();
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        var width = (int)ViewSize.Width;
        var footer = (_confirming
                ? " y:delete  any other key:cancel "
                : " Space:mark  a:keep oldest  n:keep newest  u:unmark  Del:delete  g:go to  r:rescan  Esc:close ")
            .FitHints(Math.Max(0, width - 4));
        var bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, footer), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }
}

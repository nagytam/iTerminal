using iConsole.Buffers;
using iConsole.Controls.Charts;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Services.DiskUsage;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Ctrl+Shift+F12: where the space went under a folder — every level sorted largest first, each row a
/// bar of its share of the folder it is in, with its size and file count. Enter goes into a folder,
/// Backspace or ← comes out, <c>g</c> takes the panel there, <c>s</c> sorts by name instead, <c>r</c>
/// measures again.
///
/// <para>The walk (<see cref="IDiskUsageService"/>) runs on a background task and the dialog says how far
/// it is while it does; closing the dialog cancels it. The result is a tree kept in memory, so moving
/// around it afterwards costs nothing — only <c>r</c> reads the disk again. Local folders only: the
/// other drives list one level per request, which is not a thing to walk for a picture.</para>
/// </summary>
public class DiskUsageDialog : Dialog
{
    private const int BarWidth = 16;

    private readonly IDiskUsageService _service;
    private readonly IContentModelService? _contentModelService;
    private readonly InfoLines _lines;
    private DiskUsageScan? _scan;
    private DiskUsageNode? _current;
    private string? _panelName;
    private int _cursor;
    private int _top;
    private bool _sortByName;
    private bool _announcedEnd;
    private readonly Dictionary<DiskUsageNode, int> _cursorOf = [];

    public DiskUsageDialog(
        IDiskUsageService service,
        IThemeService themeService,
        IContentModelService? contentModelService = null)
        : base(nameof(DiskUsageDialog), themeService)
    {
        _service = service;
        _contentModelService = contentModelService;
        Title = "Disk usage";
        _lines = new InfoLines(themeService);
        AddControl(_lines);
    }

    /// <summary>The scan behind the dialog, for a test to wait on.</summary>
    public DiskUsageScan? Scan => _scan;

    /// <summary>The folder whose children are listed now.</summary>
    public DiskUsageNode? Current => _current;

    /// <summary>The row under the cursor, or null while scanning or with nothing there.</summary>
    public DiskUsageNode? Selected
    {
        get
        {
            var rows = Rows();
            return _cursor >= 0 && _cursor < rows.Count ? rows[_cursor] : null;
        }
    }

    /// <summary>Sets the folder to measure and the panel <c>g</c> navigates; call before showing the dialog.</summary>
    public void Load(string folderPath, string? panelName)
    {
        _scan?.Cancel();
        _panelName = panelName;
        StartScan(folderPath);
    }

    private void StartScan(string path)
    {
        _scan?.Cancel();
        _scan = _service.Start(path);
        _current = null;
        _cursor = 0;
        _top = 0;
        _announcedEnd = false;
        _cursorOf.Clear();
        UpdateLines();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            var width = (int)Math.Min(Math.Max(60, Parent.ViewSize.Width - 6), 130);
            var height = (int)Math.Max(12, Parent.ViewSize.Height - 4);
            CenterInParent(new Size(width, height));
        }

        _lines.Position = new Position(2, 1);
        _lines.ViewSize = new Size(Math.Max(1, ViewSize.Width - 4), Math.Max(1, ViewSize.Height - 2));
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
        else if (!_announcedEnd)
        {
            _announcedEnd = true;
            _current = _scan.Root;
            _cursor = 0;
            _top = 0;
            UpdateLines();
        }
    }

    private int VisibleRows => Math.Max(1, (int)_lines.ViewSize.Height - 3);

    private List<DiskUsageNode> Rows()
    {
        if (_current is null)
        {
            return [];
        }

        IEnumerable<DiskUsageNode> rows = _current.Children;
        if (_sortByName)
        {
            rows = rows
                .OrderBy(n => n.IsOther)
                .ThenByDescending(n => n.IsFolder)
                .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase);
        }

        return rows.ToList();
    }

    private static string Percent(long part, long whole) =>
        whole <= 0 ? "  0%" : $"{Math.Min(100d, 100d * part / whole),3:0}%";

    private static string Bar(long part, long whole) =>
        BarChart.BuildBar(whole <= 0 ? 0 : (double)part / whole, BarWidth).Replace(' ', '░');

    private void UpdateLines()
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        var width = (int)Math.Max(20, _lines.ViewSize.Width);
        var lines = new List<InfoLines.Line>();

        if (_scan is null)
        {
            lines.Add(new("Nothing to measure.", Color.Yellow));
            _lines.Lines = lines;
            StateHasChanged();
            return;
        }

        if (!_scan.IsComplete)
        {
            lines.Add(new($"Measuring {_scan.Path}", colorScheme.Foreground));
            lines.Add(new($"{_scan.FilesScanned:N0} files, {_scan.BytesScanned.ToFileSize()} so far", Color.Yellow));
            lines.Add(new(_scan.CurrentFolder, Color.Cyan));
            lines.Add(default);
            lines.Add(new("Esc stops and closes.", colorScheme.Foreground));
            _lines.Lines = lines;
            StateHasChanged();
            return;
        }

        if (_current is null)
        {
            lines.Add(new(_scan.WasCancelled ? "Cancelled." : $"Could not measure {_scan.Path}.", Color.Yellow));
            _lines.Lines = lines;
            StateHasChanged();
            return;
        }

        var root = _scan.Root!;
        lines.Add(new(
            $"{_current.Path}",
            Color.Cyan));
        lines.Add(new(
            $"{_current.Size.ToFileSize()} in {_current.FileCount:N0} files, {_current.FolderCount:N0} folders"
            + (_current.Inaccessible ? "  (some of it could not be read)" : string.Empty)
            + (_current == root ? string.Empty : $"   — {Percent(_current.Size, root.Size).Trim()} of {root.Name}"),
            colorScheme.Foreground));
        lines.Add(default);

        var rows = Rows();
        _cursor = Math.Clamp(_cursor, 0, Math.Max(0, rows.Count - 1));
        var visible = VisibleRows;
        if (_cursor < _top)
        {
            _top = _cursor;
        }
        else if (_cursor >= _top + visible)
        {
            _top = _cursor - visible + 1;
        }

        var nameWidth = Math.Max(8, width - (2 + BarWidth + 2 + 10 + 2 + 4 + 2 + 10));
        if (rows.Count == 0)
        {
            lines.Add(new("  (empty)", colorScheme.Foreground));
        }

        for (var i = _top; i < rows.Count && i < _top + visible; i++)
        {
            var row = rows[i];
            var name = row.IsFolder ? row.Name + "/" : row.Name;
            var files = row.IsFolder ? $"{row.FileCount:N0}" : string.Empty;
            var text = $"{(i == _cursor ? "▶" : " ")} {name.Cut(nameWidth).ExpandRight(nameWidth)} "
                + $"{Bar(row.Size, _current.Size)} {row.Size.ToFileSize(),10} {Percent(row.Size, _current.Size)} {files,10}";
            Color? color = i == _cursor
                ? Color.Yellow
                : row.IsFolder ? Color.Cyan : row.IsOther ? Color.Gray : null;
            lines.Add(new(text, color));
        }

        _lines.Lines = lines;
        StateHasChanged();
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (!keyEvent.Alt && !keyEvent.Control && _scan is { IsComplete: true } && _current is not null)
        {
            var rows = Rows();
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.UpArrow:
                    _cursor = Math.Max(0, _cursor - 1);
                    UpdateLines();
                    return true;
                case ConsoleKey.DownArrow:
                    _cursor = Math.Min(Math.Max(0, rows.Count - 1), _cursor + 1);
                    UpdateLines();
                    return true;
                case ConsoleKey.PageUp:
                    _cursor = Math.Max(0, _cursor - VisibleRows);
                    UpdateLines();
                    return true;
                case ConsoleKey.PageDown:
                    _cursor = Math.Min(Math.Max(0, rows.Count - 1), _cursor + VisibleRows);
                    UpdateLines();
                    return true;
                case ConsoleKey.Home:
                    _cursor = 0;
                    UpdateLines();
                    return true;
                case ConsoleKey.End:
                    _cursor = Math.Max(0, rows.Count - 1);
                    UpdateLines();
                    return true;
                case ConsoleKey.Enter:
                case ConsoleKey.RightArrow:
                    Enter();
                    return true;
                case ConsoleKey.Backspace:
                case ConsoleKey.LeftArrow:
                    GoUp();
                    return true;
                case ConsoleKey.S:
                    _sortByName = !_sortByName;
                    _cursor = 0;
                    _top = 0;
                    UpdateLines();
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

    /// <summary>Goes into the folder under the cursor.</summary>
    public void Enter()
    {
        if (Selected is { IsFolder: true } folder && _current is not null)
        {
            _cursorOf[_current] = _cursor;
            _current = folder;
            _cursor = 0;
            _top = 0;
            UpdateLines();
        }
    }

    /// <summary>Comes out to the folder this one is in, with the cursor back on it.</summary>
    public void GoUp()
    {
        if (_current?.Parent is { } parent)
        {
            var came = _current;
            _current = parent;
            var index = Rows().IndexOf(came);
            _cursor = index >= 0 ? index : _cursorOf.GetValueOrDefault(parent);
            _top = 0;
            UpdateLines();
        }
    }

    /// <summary>Points the panel the view was opened over at the folder shown (or the one under the cursor), and closes.</summary>
    public async Task GoToPanelAsync()
    {
        var target = Selected is { IsFolder: true } folder ? folder.Path : _current?.Path;
        if (target is null || _contentModelService is null)
        {
            return;
        }

        var url = new Url(Container.FILE + target.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
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
        var footer = " Enter:open  ←:up  g:go to panel  s:sort  r:rescan  Esc:close ".FitHints(Math.Max(0, width - 4));
        var bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, footer), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }
}

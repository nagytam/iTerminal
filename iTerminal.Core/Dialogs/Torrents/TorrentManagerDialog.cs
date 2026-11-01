using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Helper;
using iTerminal.Core.Services.Torrents;

namespace iTerminal.Core.Dialogs.Torrents;

/// <summary>
/// The BitTorrent manager: every torrent with its progress, speed and peers, refreshed twice a second. A adds a magnet
/// link, a <c>.torrent</c> file or the URL of one; Space pauses or resumes, D asks and then removes (Y keeps the
/// downloaded files, W deletes them too), O opens the save folder in the active panel.
///
/// <para>Nothing here waits on the engine: every action runs on a background task and says how it went on the status line.</para>
/// </summary>
public class TorrentManagerDialog : Dialog
{
    private enum Mode
    {
        List,
        Add,
    }

    private const int DialogWidth = 100;
    private const int DialogHeight = 24;
    private const int ListRows = 14;
    private const int MaxFieldLength = 2000;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

    private readonly ITorrentService _torrents;
    private readonly IContentModelService _contentModelService;
    private readonly Lock _sync = new();

    private Mode _mode = Mode.List;
    private IReadOnlyList<TorrentItem> _list = [];
    private int _cursor;
    private string? _selectedId;
    private bool _confirmDelete;
    private DateTime _lastRefresh = DateTime.MinValue;

    private string _source = string.Empty;
    private string _folder = string.Empty;
    private int _field;

    private string _status = string.Empty;
    private bool _statusIsError;

    public TorrentManagerDialog(ITorrentService torrents, IContentModelService contentModelService, IThemeService themeService)
        : base(nameof(TorrentManagerDialog), themeService)
    {
        Title = "BitTorrent";
        _torrents = torrents;
        _contentModelService = contentModelService;
    }

    /// <summary>The action the last key started. Nothing waits on it; it exists so a test can.</summary>
    public Task PendingOperation { get; private set; } = Task.CompletedTask;

    public override async Task ShowAsync()
    {
        lock (_sync)
        {
            _mode = Mode.List;
            _confirmDelete = false;
            Reload();
        }

        // Brings back the torrents of an earlier run; the first call does the work, later ones return at once.
        PendingOperation = Task.Run(async () =>
        {
            try
            {
                await _torrents.RestoreAsync();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Fail(exception);
            }
        });

        await base.ShowAsync();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(DialogWidth, DialogHeight));
        }

        base.OnInitializeLayout();
    }

    public override void OnIdle()
    {
        base.OnIdle();

        if (_mode == Mode.List && DateTime.UtcNow - _lastRefresh >= RefreshInterval)
        {
            lock (_sync)
            {
                Reload();
            }

            StateHasChanged();
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_mode == Mode.Add)
        {
            yield return ("Enter", UiText.Get("DialogHint.AddTorrent", "Add the torrent"));
            yield return ("Tab / Up/Down", UiText.Get("DialogHint.MoveBetweenFields", "Move between the fields"));
            yield return ("Ctrl+V", UiText.Get("DialogHint.PasteLinkOrPath", "Paste a link or a path"));
            yield return ("Esc", UiText.Get("DialogHint.BackToList", "Back to the list"));
            yield break;
        }

        if (_confirmDelete)
        {
            yield return ("Y", UiText.Get("DialogHint.RemoveKeepFiles", "Remove, keep the downloaded files"));
            yield return ("W", UiText.Get("DialogHint.RemoveDeleteFiles", "Remove and delete the downloaded files"));
            yield return ("Any other key", UiText.Get("DialogHint.KeepTorrent", "Keep the torrent"));
            yield break;
        }

        yield return ("Up/Down", UiText.Get("DialogHint.ChooseTorrent", "Choose a torrent"));
        yield return ("A / N", UiText.Get("DialogHint.AddMagnetOrTorrent", "Add a magnet link or .torrent"));
        yield return ("Space", UiText.Get("DialogHint.PauseOrResume", "Pause or resume"));
        yield return ("D/Delete", UiText.Get("DialogHint.RemoveTorrentAsks", "Remove the torrent (asks first)"));
        yield return ("O", UiText.Get("DialogHint.OpenSaveFolder", "Open the save folder in the active panel"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        lock (_sync)
        {
            if (_mode == Mode.Add)
            {
                return HandleAddKey(keyEvent);
            }

            if (keyEvent.ConsoleKey == ConsoleKey.Escape && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift && !_confirmDelete)
            {
                // Falls through to the dialog, which closes.
            }
            else if (HandleListKey(keyEvent))
            {
                return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    #region Keys

    private bool HandleListKey(KeyEvent keyEvent)
    {
        bool plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        char letter = char.ToLowerInvariant(keyEvent.KeyChar);
        if (_confirmDelete)
        {
            // The question is answered by y or w; any other key (Esc included) only cancels it.
            _confirmDelete = false;
            if (plain && letter is 'y' or 'w')
            {
                RemoveSelected(deleteData: letter == 'w');
            }
            else
            {
                SetStatus(string.Empty, false);
            }

            StateHasChanged();
            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.UpArrow when plain:
                return MoveCursor(-1);
            case ConsoleKey.DownArrow when plain:
                return MoveCursor(1);
            case ConsoleKey.PageUp when plain:
                return MoveCursor(-ListRows);
            case ConsoleKey.PageDown when plain:
                return MoveCursor(ListRows);
            case ConsoleKey.Home:
                return MoveCursor(-int.MaxValue / 2);
            case ConsoleKey.End:
                return MoveCursor(int.MaxValue / 2);
            case ConsoleKey.Delete when plain:
                AskDelete();
                return true;
            case ConsoleKey.Spacebar when plain:
                TogglePause();
                return true;
        }

        if (!plain)
        {
            return false;
        }

        switch (letter)
        {
            case 'a':
            case 'n':
                OpenAdd();
                return true;
            case 'd':
                AskDelete();
                return true;
            case 'o':
                OpenFolder();
                return true;
        }

        return false;
    }

    private bool HandleAddKey(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Escape)
        {
            _mode = Mode.List;
            SetStatus(string.Empty, false);
            StateHasChanged();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Control && !keyEvent.Alt)
        {
            SubmitAdd();
            return true;
        }

        bool onSource = _field == 0;
        if (PasteHelper.IsDeleteWordKey(keyEvent))
        {
            SetField(PasteHelper.DeleteLastWord(onSource ? _source : _folder));
            return true;
        }

        if (PasteHelper.IsPasteKey(keyEvent))
        {
            bool source = onSource;
            PasteHelper.Paste(text =>
            {
                string line = PasteHelper.SingleLine(text);
                string joined = (source ? _source : _folder) + line;
                string clipped = joined.Length > MaxFieldLength ? joined[..MaxFieldLength] : joined;
                if (source)
                {
                    _source = clipped;
                }
                else
                {
                    _folder = clipped;
                }
            }, StateHasChanged);
            return true;
        }

        if (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.U)
        {
            SetField(string.Empty);
            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Tab when !keyEvent.Shift:
            case ConsoleKey.DownArrow:
            case ConsoleKey.Tab:
            case ConsoleKey.UpArrow:
                _field = 1 - _field;
                StateHasChanged();
                return true;
        }

        if (keyEvent.Control || keyEvent.Alt)
        {
            return true;
        }

        string current = onSource ? _source : _folder;
        if (keyEvent.ConsoleKey == ConsoleKey.Backspace)
        {
            if (current.Length > 0)
            {
                SetField(current[..^1]);
            }
        }
        else if (keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar) && current.Length < MaxFieldLength)
        {
            SetField(current + keyEvent.KeyChar);
        }

        return true;
    }

    private void SetField(string text)
    {
        if (_field == 0)
        {
            _source = text;
        }
        else
        {
            _folder = text;
        }

        StateHasChanged();
    }

    #endregion

    #region Actions

    private TorrentItem? Selected => _list.FirstOrDefault(t => t.Id == _selectedId) ?? _list.FirstOrDefault();

    private void Reload()
    {
        _lastRefresh = DateTime.UtcNow;
        _list = _torrents.Snapshot();
        int index = _selectedId is null ? -1 : _list.ToList().FindIndex(t => t.Id == _selectedId);
        _cursor = index >= 0 ? index : Math.Clamp(_cursor, 0, Math.Max(0, _list.Count - 1));
        _selectedId = _list.ElementAtOrDefault(_cursor)?.Id;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (_mode == Mode.List && !_confirmDelete && _list.Count > 0)
        {
            if (mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
            {
                lock (_sync)
                {
                    return MoveCursor(mouseEvent.WheelDelta > 0 ? -3 : 3);
                }
            }

            if (mouseEvent.LeftClicked)
            {
                long row = mouseEvent.Position.Y - (AbsolutePosition.Y + 3);
                long column = mouseEvent.Position.X - (AbsolutePosition.X + 2);
                lock (_sync)
                {
                    int first = _cursor >= ListRows ? _cursor - ListRows + 1 : 0;
                    if (row >= 0 && row < ListRows && column >= 0 && column < ViewSize.Width - 4 && first + (int)row < _list.Count)
                    {
                        return MoveCursor(first + (int)row - _cursor);
                    }
                }
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    private bool MoveCursor(int delta)
    {
        if (_list.Count > 0)
        {
            _cursor = Math.Clamp(_cursor + delta, 0, _list.Count - 1);
            _selectedId = _list[_cursor].Id;
        }

        StateHasChanged();
        return true;
    }

    private void OpenAdd()
    {
        _source = string.Empty;
        _folder = _torrents.DefaultDownloadDirectory;
        _field = 0;
        _mode = Mode.Add;
        SetStatus(Say("Torrent.Status.PasteHint", "Paste a magnet link, or give a .torrent file or its URL. Enter adds, Esc goes back."), false);
        StateHasChanged();
    }

    private void SubmitAdd()
    {
        string source = TorrentSource.Clean(_source);
        if (TorrentSource.Classify(source) == TorrentSourceKind.Invalid)
        {
            SetStatus(Say("Torrent.Status.NotATorrent", "That is not a magnet link, a .torrent file or a URL."), true);
            _field = 0;
            StateHasChanged();
            return;
        }

        string folder = _folder.Trim();
        _mode = Mode.List;
        SetStatus(Say("Torrent.Status.Adding", "Adding the torrent…"), false);
        PendingOperation = Task.Run(async () =>
        {
            try
            {
                string id = await _torrents.AddAsync(source, folder.Length == 0 ? null : folder);
                lock (_sync)
                {
                    _selectedId = id;
                    Reload();
                    SetStatus(Say("Torrent.Status.Added", "Added. It starts at once."), false);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Fail(exception);
            }

            StateHasChanged();
        });
        StateHasChanged();
    }

    private void TogglePause()
    {
        TorrentItem? item = Selected;
        if (item is null)
        {
            return;
        }

        bool resume = item.Status is TorrentStatus.Paused or TorrentStatus.Stopped;
        SetStatus(resume ? Say("Torrent.Status.Resuming", "Resuming \"{0}\"…", item.Name) : Say("Torrent.Status.Pausing", "Pausing \"{0}\"…", item.Name), false);
        PendingOperation = Task.Run(async () =>
        {
            try
            {
                await (resume ? _torrents.ResumeAsync(item.Id) : _torrents.PauseAsync(item.Id));
                lock (_sync)
                {
                    Reload();
                    SetStatus(resume ? Say("Torrent.Status.Resumed", "Resumed \"{0}\".", item.Name) : Say("Torrent.Status.Paused", "Paused \"{0}\".", item.Name), false);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Fail(exception);
            }

            StateHasChanged();
        });
        StateHasChanged();
    }

    private void AskDelete()
    {
        if (Selected is not { } item)
        {
            return;
        }

        _confirmDelete = true;
        SetStatus(string.Format(UiText.Get("Torrent.Confirm.Remove", "Remove \"{0}\"? y keeps the downloaded files, w deletes them too, anything else cancels."), item.Name), true);
        StateHasChanged();
    }

    private void RemoveSelected(bool deleteData)
    {
        if (Selected is not { } item)
        {
            return;
        }

        SetStatus(Say("Torrent.Status.Removing", "Removing \"{0}\"…", item.Name), false);
        PendingOperation = Task.Run(async () =>
        {
            try
            {
                await _torrents.RemoveAsync(item.Id, deleteData);
                lock (_sync)
                {
                    _selectedId = null;
                    Reload();
                    SetStatus(deleteData ? Say("Torrent.Status.RemovedWithFiles", "Removed \"{0}\" and its files.", item.Name) : Say("Torrent.Status.Removed", "Removed \"{0}\".", item.Name), false);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Fail(exception);
            }

            StateHasChanged();
        });
    }

    private void OpenFolder()
    {
        if (Selected is not { } item)
        {
            return;
        }

        string path = item.SavePath;
        SetStatus(Say("Torrent.Status.Opening", "Opening {0} in the panel…", path), false);
        PendingOperation = Task.Run(() => _contentModelService.SetUrlAsync(new Url(path)));
        Close();
    }

    private void Fail(Exception exception)
    {
        lock (_sync)
        {
            SetStatus(exception.Message, true);
        }

        StateHasChanged();
    }

    /// <summary>A status message in the chosen language, with its arguments filled in.</summary>
    private static string Say(string key, string english, params object[] args) => string.Format(UiText.Get(key, english), args);

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
    }

    #endregion

    #region Drawing

    internal static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.0} {units[unit]}";
    }

    internal static string FormatRate(long bytesPerSecond) => bytesPerSecond <= 0 ? "-" : FormatSize(bytesPerSecond) + "/s";

    internal static string StatusLabel(TorrentStatus status) => status switch
    {
        TorrentStatus.Starting => UiText.Get("Torrent.State.Starting", "Starting"),
        TorrentStatus.Metadata => UiText.Get("Torrent.State.Metadata", "Metadata"),
        TorrentStatus.Checking => UiText.Get("Torrent.State.Checking", "Checking"),
        TorrentStatus.Downloading => UiText.Get("Torrent.State.Downloading", "Down"),
        TorrentStatus.Seeding => UiText.Get("Torrent.State.Seeding", "Seeding"),
        TorrentStatus.Paused => UiText.Get("Torrent.State.Paused", "Paused"),
        TorrentStatus.Stopped => UiText.Get("Torrent.State.Stopped", "Stopped"),
        _ => UiText.Get("Torrent.State.Error", "Error"),
    };

    internal static string Row(TorrentItem item, int nameWidth) =>
        $"{Clip(item.Name, nameWidth).PadRight(nameWidth)} {StatusLabel(item.Status),-8} {item.Progress,5:0.0}% {FormatSize(item.Size),9} {FormatRate(item.DownloadRate),10} {FormatRate(item.UploadRate),10} {item.Peers,4}";

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        int iw = (int)ViewSize.Width - 2;
        int ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 0)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        lock (_sync)
        {
            for (int row = 0; row < ih; row++)
            {
                Put(buffer, 0, row, iw, string.Empty, colorScheme.Foreground, colorScheme.Background);
            }

            if (_mode == Mode.List)
            {
                DrawList(buffer, iw, colorScheme);
            }
            else
            {
                DrawForm(buffer, iw, colorScheme);
            }

            Put(buffer, 1, ih - 2, iw - 2, _status, _statusIsError ? colorScheme.Error : Color.Cyan, colorScheme.Background);
            Put(buffer, 1, ih - 1, iw - 2, Hints(), colorScheme.Foreground, colorScheme.Background);
        }
    }

    private string Hints() => _mode switch
    {
        Mode.List => _confirmDelete ? UiText.Get("Torrent.Hint.Confirm", "y remove  w remove + delete files  any other key cancels")
            : UiText.Get("Torrent.Hint.List", "A add  Space pause/resume  D remove  O open folder  Esc close"),
        _ => UiText.Get("Torrent.Hint.Edit", "Tab field  Enter add  Ctrl+U clear  Esc back"),
    };

    private void DrawList(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        long down = _list.Sum(t => t.DownloadRate);
        long up = _list.Sum(t => t.UploadRate);
        Put(buffer, 1, 0, iw - 2, $"{_list.Count} torrents  ·  down {FormatRate(down)}  ·  up {FormatRate(up)}", Color.Cyan, colorScheme.Background);

        int nameWidth = Math.Max(10, iw - 2 - 2 - 53);
        Put(buffer, 1, 1, iw - 2, "  " + UiText.Get("Torrent.Col.Name", "Name").PadRight(nameWidth) + " " + UiText.Get("Torrent.Col.State", "State").PadRight(8) + " "
            + UiText.Get("Torrent.Col.Done", "Done").PadLeft(6) + " " + UiText.Get("Torrent.Col.Size", "Size").PadLeft(9) + " " + UiText.Get("Torrent.Col.Down", "Down").PadLeft(10) + " "
            + UiText.Get("Torrent.Col.Up", "Up").PadLeft(10) + " " + UiText.Get("Torrent.Col.Peer", "Peer").PadLeft(4), colorScheme.DisabledForeground, colorScheme.Background);
        if (_list.Count == 0)
        {
            Put(buffer, 1, 2, iw - 2, iConsole.Localization.UiText.Get("Torrents.Empty", "No torrents yet. A adds a magnet link, a .torrent file or its URL."), colorScheme.DisabledForeground, colorScheme.Background);
        }

        int first = _cursor >= ListRows ? _cursor - ListRows + 1 : 0;
        for (int i = 0; i < ListRows; i++)
        {
            int index = first + i;
            if (index >= _list.Count)
            {
                continue;
            }

            bool highlighted = index == _cursor;
            Put(buffer, 1, 2 + i, iw - 2, (highlighted ? "> " : "  ") + Row(_list[index], nameWidth),
                highlighted ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground,
                highlighted ? colorScheme.SelectedFocusedBackground : colorScheme.Background);
        }

        if (Selected is { } selected)
        {
            int y = 2 + ListRows;
            Put(buffer, 1, y, iw - 2, ProgressBar(selected.Progress, Math.Min(40, iw - 12)) + $" {selected.Progress:0.0}%", colorScheme.Foreground, colorScheme.Background);
            Put(buffer, 1, y + 1, iw - 2, selected.Error is { } error ? string.Format(iConsole.Localization.UiText.Get("Torrents.Error", "Error: {0}"), error) : string.Format(iConsole.Localization.UiText.Get("Torrents.SavedIn", "Saved in {0}"), selected.SavePath), selected.Error is null ? colorScheme.DisabledForeground : colorScheme.Error, colorScheme.Background);
        }
    }

    internal static string ProgressBar(double percent, int width)
    {
        if (width < 3)
        {
            return string.Empty;
        }

        int filled = (int)Math.Round(Math.Clamp(percent, 0, 100) / 100 * (width - 2));
        return "[" + new string('#', filled) + new string('-', width - 2 - filled) + "]";
    }

    private void DrawForm(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        Put(buffer, 1, 0, iw - 2, iConsole.Localization.UiText.Get("Dialog.AddTorrent", "Add a torrent"), Color.Cyan, colorScheme.Background);
        string[] labels =
        [
            iConsole.Localization.UiText.Get("Field.MagnetFileOrUrl", "Magnet / file / URL"),
            iConsole.Localization.UiText.Get("Field.SaveTo", "Save to"),
        ];
        string[] values = [_source, _folder];
        for (int i = 0; i < 2; i++)
        {
            bool focused = i == _field;
            int row = 2 + i * 2;
            Put(buffer, 1, row, iw - 2, labels[i], focused ? Color.Cyan : colorScheme.DisabledForeground, colorScheme.Background);
            string value = values[i] + (focused ? "_" : string.Empty);
            // The end of a long link is what is being typed, so that is the part kept in view.
            string shown = value.Length > iw - 4 ? value[^(iw - 4)..] : value;
            Put(buffer, 1, row + 1, iw - 2, shown, colorScheme.Foreground, focused ? colorScheme.SelectedBackground : colorScheme.Background);
        }
    }

    private void Put(IBuffer buffer, int x, int y, int width, string text, Color foreground, Color background)
    {
        if (width <= 0)
        {
            return;
        }

        string clipped = text.Length > width ? text[..width] : text.PadRight(width);
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1 + x, AbsolutePosition.Y + 1 + y),
            new Size(width, 1),
            new ConxelString(clipped, foreground, background));
    }

    private static string Clip(string text, int width) => text.Length <= width ? text : text[..(width - 1)] + "…";

    #endregion

    #region Test seams

    internal string ModeForTest { get { lock (_sync) return _mode.ToString(); } }
    internal string StatusForTest { get { lock (_sync) return _status; } }
    internal int CursorForTest { get { lock (_sync) return _cursor; } }
    internal int ListCountForTest { get { lock (_sync) return _list.Count; } }
    internal void RefreshForTest() { lock (_sync) Reload(); }

    #endregion
}

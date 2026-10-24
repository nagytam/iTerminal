using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Services.Search;
using System.Globalization;

namespace iTerminal.Core.Dialogs.Search;

/// <summary>
/// F7: find files by name and, optionally, by what they contain.
///
/// <para>Three fields and a switch — <i>Find</i> (name patterns, <c>*.cs;*.md</c>, or a plain word
/// that matches anywhere in a name), <i>Containing</i> (text, case-insensitive), <i>In</i> (the
/// folder, the focused panel's to begin with) and <i>Include subfolders</i>. Enter in any of them
/// starts the search; the results stream into the list below while it runs.</para>
///
/// <para>The search itself belongs to <see cref="IFileSearchService"/>, not to the dialog, so its
/// results outlive it: <b>Ctrl+P</b> puts them in the focused panel as the <c>search://</c> drive,
/// where every file operation works on them, and closes the dialog with the search still running
/// if it is. Enter on a result goes to it — its folder, cursor on the file — the same thing Enter
/// on a row of that panel does. Closing the dialog any other way stops a running search: a search
/// nobody can see the results of is only spending the disk.</para>
///
/// <para>Nothing here walks the disk on the key handler. Starting a search returns at once; the
/// list is refreshed from <see cref="OnIdle"/> when the service's version stamp has moved.</para>
/// </summary>
public class FindFilesDialog : SplitPaneDialog
{
    private sealed class HitListBox : ListBox<FileSearchHit>
    {
        public HitListBox(IListBoxModel<FileSearchHit> listModel, IThemeService themeService)
            : base(listModel, themeService)
        {
            ListModel.TransformItem = (hit, _) => new ListItem<FileSearchHit>(hit) { Value = " " + Format(hit), IsEnabled = true };
        }

        internal string StartFolder { get; set; } = string.Empty;

        private string Format(FileSearchHit hit)
        {
            var path = StartFolder.Length > 0 ? Path.GetRelativePath(StartFolder, hit.Path) : hit.Path;
            if (hit.IsFolder)
            {
                return path + Path.DirectorySeparatorChar;
            }

            return hit.LineNumber > 0 ? $"{path}:{hit.LineNumber}: {hit.LineText}" : path;
        }
    }

    private readonly IFileSearchService _searchService;
    private readonly IContentModelService _contentModelService;

    private readonly TextBox _namesBox;
    private readonly TextBox _textBox;
    private readonly TextBox _folderBox;
    private readonly TextBox _largerBox;
    private readonly TextBox _olderBox;
    private readonly CheckBox _subfoldersBox;
    private readonly HitListBox _resultListBox;
    private readonly ConcurrentObservableCollection<FileSearchHit> _resultItems = new();

    private int _shownVersion = -1;
    private string _message = string.Empty;
    private bool _handedToPanel;

    public FindFilesDialog(
        IFileSearchService searchService,
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(FindFilesDialog), themeService)
    {
        _searchService = searchService;
        _contentModelService = contentModelService;
        Title = "Find Files";

        _namesBox = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Find", MaxLength = 512, Placeholder = "name or pattern, e.g. *.log" };
        _textBox = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Containing", MaxLength = 512, Placeholder = "text inside the files (optional)" };
        _folderBox = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "In", MaxLength = 1024, Placeholder = "folder to search" };
        _largerBox = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Larger MB", MaxLength = 12, Placeholder = "blank: any" };
        _olderBox = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Older days", MaxLength = 6, Placeholder = "blank: any" };
        _subfoldersBox = new CheckBox(themeService) { Label = "Include subfolders", IsChecked = true, HotKeyChar = 'I' };
        _resultListBox = new HitListBox(new ListBoxModel<FileSearchHit>(_resultItems), themeService)
        {
            EmptyText = " Type a name pattern and press Enter.",
        };

        // The name box first: the layout pass focuses the first focusable child, and the name is
        // what most searches are.
        AddControl(_namesBox);
        AddControl(_textBox);
        AddControl(_folderBox);
        AddControl(_largerBox);
        AddControl(_olderBox);
        AddControl(_subfoldersBox);
        AddControl(_resultListBox);
    }

    private sealed record CriteriaPreset(double? LargerThanMegabytes, int? UnchangedForDays);

    private CriteriaPreset? _preset;

    /// <summary>
    /// The size and age the next opening starts with: the large and old file finders call this before the dialog shows, and
    /// the name box starts empty. Null for both leaves it to the last search.
    /// </summary>
    public void Preset(double? largerThanMegabytes, int? unchangedForDays) =>
        _preset = new CriteriaPreset(largerThanMegabytes, unchangedForDays);

    /// <summary>The folder a search starts in when the focused panel is not on the local disk.</summary>
    internal static string DefaultFolder => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        _handedToPanel = false;
        _shownVersion = -1;

        // The last search's words come back — refining a search is the common second step — but the
        // folder is always the panel's: the dialog is opened from where the user is now.
        var previous = _searchService.Query;
        _namesBox.Text = previous?.NamePatterns ?? string.Empty;
        _textBox.Text = previous?.ContainingText ?? string.Empty;
        _subfoldersBox.IsChecked = previous?.IncludeSubfolders ?? true;
        _folderBox.Text = PanelFolder();
        _largerBox.Text = string.Empty;
        _olderBox.Text = string.Empty;
        if (_preset is { } preset)
        {
            // A finder command asked for the search: its size and age, and no name, whatever the last search was.
            _namesBox.Text = string.Empty;
            _largerBox.Text = preset.LargerThanMegabytes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            _olderBox.Text = preset.UnchangedForDays?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            _preset = null;
        }

        _message = _searchService.IsRunning ? string.Empty : _searchService.StatusMessage;
        SyncResults();
    }

    /// <summary>
    /// The panel the dialog was opened from. Not <c>GetFocusedModel</c>: opening a dialog takes focus
    /// away from the panel, which clears the focused-panel name — the last-focused one is the panel
    /// the user was in.
    /// </summary>
    private IContentModel? Panel => _contentModelService.GetLastContentModel() ?? _contentModelService.GetFocusedModel();

    private string PanelFolder()
    {
        var url = Panel?.Url;
        return url is not null && url.ContainerType == ContainerType.FileSystem && Directory.Exists(url.FullName)
            ? url.FullName.TrimEnd(Path.DirectorySeparatorChar)
            : DefaultFolder;
    }

    #region Actions

    /// <summary>Starts the search the fields describe. Returns at once; the walk runs on a background task.</summary>
    internal void StartSearch()
    {
        var folder = (_folderBox.Text ?? string.Empty).Trim().Trim('"');
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            _message = folder.Length == 0 ? "Say where to search." : $"No such folder: {folder}";
            StateHasChanged();
            return;
        }

        if (!TryParseOptional(_largerBox.Text, out var largerMegabytes) || !TryParseOptional(_olderBox.Text, out var olderDays))
        {
            _message = "Larger MB and Older days are numbers, or blank.";
            StateHasChanged();
            return;
        }

        var query = new FileSearchQuery(
            Path.GetFullPath(folder),
            _namesBox.Text ?? string.Empty,
            _textBox.Text ?? string.Empty,
            _subfoldersBox.IsChecked,
            largerMegabytes is { } megabytes ? (long)(megabytes * 1024 * 1024) : null,
            olderDays is { } days ? DateTime.Now.AddDays(-days) : null);
        _message = string.Empty;
        _ = _searchService.StartAsync(query);
        _resultListBox.SetFocus();
        StateHasChanged();
    }

    /// <summary>A number the field may leave blank (meaning none). False when it is not a number, or is negative.</summary>
    private static bool TryParseOptional(string? text, out double? value)
    {
        value = null;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number < 0)
        {
            return false;
        }

        value = number;
        return true;
    }

    private FileSearchHit? SelectedHit()
    {
        var items = _resultListBox.ListModel.ListItems;
        var index = (int)_resultListBox.ScrollableModel.CursorPosition.Y;
        return index >= 0 && index < items.Count ? items[index].Item : null;
    }

    /// <summary>Closes the dialog and points the focused panel at the selected hit's folder, cursor on the hit.</summary>
    internal async Task RevealSelectedAsync()
    {
        var hit = SelectedHit();
        var panel = Panel;
        if (hit is null || panel is null)
        {
            return;
        }

        var folder = hit.IsFolder ? Path.GetDirectoryName(hit.Path.TrimEnd(Path.DirectorySeparatorChar)) : Path.GetDirectoryName(hit.Path);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        var panelName = panel.Name;
        Close();
        await _contentModelService.SetUrlAsync(panelName, new Url(folder + Path.DirectorySeparatorChar));
        var target = hit.IsFolder ? hit.Path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar : hit.Path;
        _contentModelService.ContentModels.FirstOrDefault(m => m.Name == panelName)?.RestoreNavigation(new Url(target).FullUrl, 0, 0);
    }

    /// <summary>Closes the dialog and shows the results in the focused panel, where every file key works on them.</summary>
    internal async Task SendToPanelAsync()
    {
        var panel = Panel;
        if (panel is null || _searchService.Query is null)
        {
            return;
        }

        _handedToPanel = true;
        var panelName = panel.Name;
        Close();
        await _contentModelService.SetUrlAsync(panelName, new Url(Container.SEARCH_RESULTS));
    }

    public override void Close()
    {
        if (!_handedToPanel)
        {
            _searchService.Cancel();
        }

        base.Close();
    }

    #endregion

    #region Input

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Control && !keyEvent.Alt)
        {
            if (_resultListBox.IsFocused && SelectedHit() is not null)
            {
                await RevealSelectedAsync();
            }
            else
            {
                StartSearch();
            }

            return true;
        }

        if (keyEvent.Control && !keyEvent.Alt)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.P:
                    await SendToPanelAsync();
                    return true;

                case ConsoleKey.X:
                    _searchService.Cancel();
                    return true;
            }
        }

        // Home and End belong to the text boxes' cursors, so Ctrl+Home and Ctrl+End reach the first and the last result.
        if ((_namesBox.IsFocused || _textBox.IsFocused || _folderBox.IsFocused)
            && keyEvent.Control && !keyEvent.Alt
            && keyEvent.ConsoleKey is ConsoleKey.Home or ConsoleKey.End
            && _resultListBox.Count > 0)
        {
            return await _resultListBox.HandleKeyEventAsync(new KeyEvent(keyEvent.KeyChar, keyEvent.ConsoleKey, false, false, false));
        }

        // The arrows drive the results from the text boxes too, so a search and a pick from its
        // results is one motion; Tab still moves between the fields.
        if ((_namesBox.IsFocused || _textBox.IsFocused || _folderBox.IsFocused)
            && keyEvent.ConsoleKey is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.PageUp or ConsoleKey.PageDown
            && _resultListBox.Count > 0)
        {
            return await _resultListBox.HandleKeyEventAsync(keyEvent);
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void OnIdle()
    {
        SyncResults();
        base.OnIdle();
    }

    /// <summary>Copies the service's hits into the list when they have changed since the last look.</summary>
    private void SyncResults()
    {
        var version = _searchService.Version;
        if (version == _shownVersion)
        {
            return;
        }

        _shownVersion = version;
        var cursor = (int)_resultListBox.ScrollableModel.CursorPosition.Y;
        var scroll = (int)_resultListBox.ScrollableModel.ScrollPosition.Y;
        _resultListBox.StartFolder = _searchService.Query?.StartFolder ?? string.Empty;

        var hits = _searchService.Hits;
        _resultItems.ReplaceAll([.. hits]);
        _resultListBox.UpdateModel();
        _resultListBox.EmptyText = _searchService.IsRunning ? " Searching…"
            : _searchService.Query is null ? " Type a name pattern and press Enter."
            : " Nothing found.";

        // Hits only ever arrive at the end, so the row the user is on stays where it is.
        var max = Math.Max(0, hits.Count - 1);
        _resultListBox.ScrollableModel.CursorPosition.Y = Math.Clamp(cursor, 0, max);
        _resultListBox.ScrollableModel.ScrollPosition.Y = Math.Clamp(scroll, 0, max);
        StateHasChanged();
    }

    #endregion

    #region Test seams

    internal IReadOnlyList<FileSearchHit> ResultsForTest => [.. _resultItems.AsSnapshot()];
    internal string MessageForTest => _message;
    internal TextBox NamesBoxForTest => _namesBox;
    internal TextBox TextBoxForTest => _textBox;
    internal TextBox FolderBoxForTest => _folderBox;
    internal ListBox<FileSearchHit> ResultListBoxForTest => _resultListBox;

    #endregion

    #region Layout and drawing

    private const int FieldRows = 4;

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.95);
        }

        var innerWidth = (int)ViewSize.Width - 2;
        var innerHeight = (int)ViewSize.Height - 2;
        if (innerWidth > 0 && innerHeight > 0)
        {
            // Rows 0-3 the fields, 4 the status line, 5 the results header, the last row the keys.
            _namesBox.Position = new Position(1, 1);
            _textBox.Position = new Position(1, 2);
            _folderBox.Position = new Position(1, 3);
            foreach (var box in new[] { _namesBox, _textBox, _folderBox })
            {
                box.ViewSize = new Size(innerWidth, 1);
                box.ScrollableModel.ViewSize = box.ViewSize;
            }

            _subfoldersBox.Position = new Position(1, 4);
            _subfoldersBox.ViewSize = new Size(Math.Min(innerWidth, 30), 1);

            // The size and the age share the subfolders' row: the boxes sit to its right when there is room for them.
            _largerBox.Position = new Position(33, 4);
            _largerBox.ViewSize = new Size(Math.Clamp(innerWidth - 33 - 2, 0, 22), 1);
            _olderBox.Position = new Position(Math.Min(innerWidth, 33 + 24), 4);
            _olderBox.ViewSize = new Size(Math.Clamp(innerWidth - 33 - 24 - 1, 0, 20), 1);
            foreach (var box in new[] { _largerBox, _olderBox })
            {
                box.ScrollableModel.ViewSize = box.ViewSize;
            }

            var listTop = FieldRows + 2;
            _resultListBox.Position = new Position(1, listTop + 1);
            _resultListBox.ViewSize = new Size(innerWidth, Math.Max(0, innerHeight - listTop - 1));
        }

        base.OnInitializeLayout();
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        var x = (int)AbsolutePosition.X + 1;
        var y = (int)AbsolutePosition.Y + 1;
        var innerWidth = (int)ViewSize.Width - 2;
        var innerHeight = (int)ViewSize.Height - 2;
        if (innerWidth <= 0 || innerHeight <= 0)
        {
            return;
        }

        // The check box is narrower than the row; the rest of its row is the dialog's to clear.
        var checkWidth = (int)_subfoldersBox.ViewSize.Width;
        WriteLine(buffer, x + checkWidth, y + FieldRows - 1, innerWidth - checkWidth, string.Empty, colorScheme.Foreground, colorScheme.Background);

        DrawStatus(buffer, x, y + FieldRows, innerWidth, colorScheme);
        DrawSectionHeader(buffer, x, y + FieldRows + 1, innerWidth, $"Results ({_resultListBox.Count:N0})", _resultListBox.IsFocused, colorScheme);

        var running = _searchService.IsRunning;
        var hints = running
            ? "Enter:go to  Ctrl+P:to panel  Ctrl+X:stop  Tab:next field  Esc:close"
            : "Enter:search / go to  Ctrl+P:results to panel  Tab:next field  Space:toggle  Esc:close";
        WriteLine(buffer, x, y + innerHeight - 1, innerWidth, hints.FitHints(innerWidth), colorScheme.Foreground, colorScheme.Background);
    }

    private void DrawStatus(IBuffer buffer, int x, int y, int width, ColorScheme colorScheme)
    {
        string text;
        var foreground = colorScheme.Foreground;
        if (_message.Length > 0)
        {
            text = _message;
            foreground = colorScheme.Error;
        }
        else if (_searchService.IsRunning)
        {
            var progress = _searchService.Progress;
            text = $"Searching… {progress.Hits:N0} found in {progress.FilesScanned:N0} files — {progress.CurrentFolder}";
            foreground = Color.Yellow;
        }
        else
        {
            text = _searchService.StatusMessage;
            if (text.StartsWith("Search failed", StringComparison.Ordinal))
            {
                foreground = colorScheme.Error;
            }
        }

        WriteLine(buffer, x, y, width, text.Shrink(width) ?? string.Empty, foreground, colorScheme.Background);
    }

    #endregion
}

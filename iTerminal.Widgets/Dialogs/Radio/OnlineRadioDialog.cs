using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Helper;
using iTerminal.Widgets.Model.Radio;
using iTerminal.Core.Services.Music;

namespace iTerminal.Widgets.Dialogs.Radio;

/// <summary>
/// Online radio: the internet stations the person listens to, played through the shared music player.
///
/// <para>Enter plays the highlighted station. The station replaces the music queue, because a live stream has no
/// end and "next" on it would only mean switching to another station; Space pauses and resumes, S stops. F marks a
/// favourite, and favourites are listed first. V shows the favourites alone. R shows the last five stations played,
/// most recent first. / filters by name, genre or address, as the contact manager does. N adds a station, E changes
/// it and D removes it after a y/n.</para>
/// </summary>
public class OnlineRadioDialog : Dialog
{
    private const int DialogWidth = 78;
    private const int DialogHeight = 22;
    private const int MaxListRows = 12;
    private const int MaxFieldLength = 200;
    private const int FieldCount = 3;
    private const int NameField = 0;
    private const int GenreField = 1;
    private const int UrlField = 2;
    private const int LabelWidth = 10;
    private const int GenreColumn = 16;
    // The marker is three columns of icons, each followed by a space: a favourite star, the on-air arrow and the genre
    // icon. Then the name.
    private const int MarkerWidth = 5;

    /// <summary>Rows the list may use: up to twelve, fewer on a short terminal, so the details, the message and the footer stay inside the frame.</summary>
    private int ListRows => Math.Clamp((int)ViewSize.Height - 2 - 8, 3, MaxListRows);

    /// <summary>The first row under the list: the address and genre of the highlighted station, or the form's three fields.</summary>
    private int DetailsTop => ListRows + 2;

    private int MessageRow => DetailsTop + FieldCount;

    private enum Mode
    {
        Browse,
        Form,
        Filter,
        ConfirmDelete,
    }

    /// <summary>Which stations the list shows. Kept across openings of the dialog.</summary>
    private enum ListView
    {
        All,
        Favourites,
        Recent,
    }

    private readonly RadioStationStore _store;
    private readonly IMusicPlayerService _player;

    private Mode _mode = Mode.Browse;

    /// <summary>The station the highlight is on. Kept by identity, so it survives the favourites being re-sorted.</summary>
    private Guid? _selectedId;

    private string _filter = string.Empty;

    private ListView _view = ListView.All;

    /// <summary>The name, genre and address being typed while the form is open.</summary>
    private readonly string[] _values = new string[FieldCount];
    private int _field;
    private string? _error;

    /// <summary>The station the form is changing, or null when it is typing a new one.</summary>
    private RadioStation? _editing;

    /// <summary>A one-line result of the last action, such as a favourite being marked.</summary>
    private string? _notice;

    /// <summary>What the footer's now-playing line showed at the last idle, so the dialog repaints only when it changes.</summary>
    private string _lastPlaybackSignature = string.Empty;

    public OnlineRadioDialog(RadioStationStore store, IMusicPlayerService player, IThemeService themeService)
        : base(nameof(OnlineRadioDialog), themeService)
    {
        Title = UiText.Get("Radio.Title", "Online radio");
        _store = store;
        _player = player;
    }

    /// <summary>The dialog is built once and opened many times, so the filter and any open form or question are cleared each time.</summary>
    public override async Task ShowAsync()
    {
        _mode = Mode.Browse;
        _filter = string.Empty;
        _error = null;
        _editing = null;
        _notice = null;
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

    private IReadOnlyList<RadioStation> Visible() => _view switch
    {
        ListView.Recent => _store.Recent().Where(s => s.Matches(_filter)).ToList(),
        _ => _store.Find(_filter, _view == ListView.Favourites),
    };

    private RadioStation? Selected
    {
        get
        {
            var visible = Visible();
            return visible.FirstOrDefault(s => s.Id == _selectedId) ?? visible.FirstOrDefault();
        }
    }

    /// <summary>True when the player is on this station's stream, playing or paused.</summary>
    private bool IsOnAir(RadioStation station) =>
        _player.State != PlaybackState.Stopped
        && string.Equals(_player.CurrentTrack?.Path, station.Url, StringComparison.Ordinal);

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        switch (_mode)
        {
            case Mode.Form:
                yield return ("Enter/Tab", UiText.Get("DialogHint.NextFieldEnterSaves", "Next field; Enter on the last one saves"));
                yield return ("Shift+Tab", UiText.Get("DialogHint.PreviousField", "Previous field"));
                yield return ("Up/Down", UiText.Get("DialogHint.MoveBetweenFields", "Move between the fields"));
                yield return ("Esc", UiText.Get("Hint.DiscardChanges", "Discard the changes"));
                yield break;
            case Mode.Filter:
                yield return ("Enter", UiText.Get("DialogHint.KeepFilterBackToList", "Keep the filter and go back to the list"));
                yield return ("Esc", UiText.Get("DialogHint.ClearFilter", "Clear the filter"));
                yield break;
            case Mode.ConfirmDelete:
                yield return ("Y", UiText.Get("DialogHint.DeleteStation", "Delete the station"));
                yield return ("Any other key", UiText.Get("DialogHint.KeepStation", "Keep the station"));
                yield break;
        }

        yield return ("Enter", UiText.Get("DialogHint.PlayStation", "Play the station"));
        yield return ("Space", UiText.Get("DialogHint.PauseStation", "Pause or resume the station"));
        yield return ("S", UiText.Get("DialogHint.StopStation", "Stop playing"));
        yield return ("F", UiText.Get("DialogHint.ToggleFavourite", "Mark or unmark the station as a favourite"));
        yield return ("V", UiText.Get("DialogHint.FavouritesOnly", "Show the favourites only"));
        yield return ("R", UiText.Get("DialogHint.RecentOnly", "Show the last five stations played"));
        yield return ("Up/Down", UiText.Get("DialogHint.MoveOverStations", "Move over the stations"));
        yield return ("PageUp/PageDown", UiText.Get("Hint.MovePageAtATime", "Move a page at a time"));
        yield return ("Home/End", UiText.Get("DialogHint.JumpFirstLastStation", "Jump to the first or last station"));
        yield return ("Insert/N", UiText.Get("DialogHint.AddStation", "Add a station"));
        yield return ("F2/E", UiText.Get("DialogHint.ChangeStation", "Change the station"));
        yield return ("Delete/D", UiText.Get("DialogHint.DeleteStationAsks", "Delete the station (asks first)"));
        yield return ("/", UiText.Get("DialogHint.FilterStations", "Filter the stations"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        switch (_mode)
        {
            case Mode.Form:
                return HandleFormKey(keyEvent);
            case Mode.Filter:
                return HandleFilterKey(keyEvent);
            case Mode.ConfirmDelete:
                // Esc is the dialog's own: it drops the question as it closes. Any other key but y withdraws it.
                if (keyEvent.ConsoleKey != ConsoleKey.Escape)
                {
                    _mode = Mode.Browse;
                    if (keyEvent.ConsoleKey == ConsoleKey.Y && !keyEvent.Control && !keyEvent.Alt)
                    {
                        DeleteSelected();
                    }

                    StateHasChanged();
                    return true;
                }

                _mode = Mode.Browse;
                break;
        }

        // The slash is Shift+7 on some layouts, so it is read by its character, not as a plain key.
        if (keyEvent.KeyChar == '/' && !keyEvent.Control && !keyEvent.Alt)
        {
            _mode = Mode.Filter;
            StateHasChanged();
            return true;
        }

        var plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.PageUp when plain:
                return MoveSelection(-ListRows);
            case ConsoleKey.PageDown when plain:
                return MoveSelection(ListRows);
            case ConsoleKey.UpArrow when plain:
                return MoveSelection(-1);
            case ConsoleKey.DownArrow when plain:
                return MoveSelection(1);
            case ConsoleKey.Home when plain:
                return SelectAt(0);
            case ConsoleKey.End when plain:
                return SelectAt(Visible().Count - 1);
            case ConsoleKey.Enter when plain:
                PlaySelected();
                return true;
            case ConsoleKey.Spacebar when plain:
                TogglePlayback();
                return true;
            case ConsoleKey.Insert when plain:
                OpenForm(null);
                return true;
            case ConsoleKey.F2 when plain:
                OpenForm(Selected);
                return true;
            case ConsoleKey.Delete when plain:
                AskToDelete();
                return true;
            case ConsoleKey.Escape when plain && _filter.Length > 0:
                // With a filter up, Esc first puts the whole list back; the next Esc closes the dialog.
                _filter = string.Empty;
                StateHasChanged();
                return true;
        }

        if (plain)
        {
            switch (char.ToLowerInvariant(keyEvent.KeyChar))
            {
                case 'n':
                    OpenForm(null);
                    return true;
                case 'e':
                    OpenForm(Selected);
                    return true;
                case 'd':
                    AskToDelete();
                    return true;
                case 'f':
                    ToggleFavourite();
                    return true;
                case 'v':
                    ToggleView(ListView.Favourites);
                    return true;
                case 'r':
                    ToggleView(ListView.Recent);
                    return true;
                case 's':
                    StopPlayback();
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>While the form is up every key belongs to it; Esc abandons the changes rather than closing the list.</summary>
    private bool HandleFormKey(KeyEvent keyEvent)
    {
        if (PasteHelper.IsDeleteWordKey(keyEvent))
        {
            _values[_field] = PasteHelper.DeleteLastWord(_values[_field]);
            _error = null;
            StateHasChanged();
            return true;
        }

        if (PasteHelper.IsPasteKey(keyEvent))
        {
            PasteHelper.Paste(text =>
            {
                var joined = _values[_field] + PasteHelper.SingleLine(text);
                _values[_field] = joined.Length > MaxFieldLength ? joined[..MaxFieldLength] : joined;
                _error = null;
            }, StateHasChanged);
            return true;
        }

        if (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.U)
        {
            _values[_field] = string.Empty;
            _error = null;
            StateHasChanged();
            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
                _mode = Mode.Browse;
                _error = null;
                _editing = null;
                break;
            case ConsoleKey.Enter:
                if (_field == FieldCount - 1)
                {
                    CommitForm();
                }
                else
                {
                    _field++;
                }

                break;
            case ConsoleKey.Tab when keyEvent.Shift:
            case ConsoleKey.UpArrow:
                _field = (_field + FieldCount - 1) % FieldCount;
                break;
            case ConsoleKey.Tab:
            case ConsoleKey.DownArrow:
                _field = (_field + 1) % FieldCount;
                break;
            case ConsoleKey.Backspace when _values[_field].Length > 0:
                _values[_field] = _values[_field][..^1];
                _error = null;
                break;
            default:
                var printable = keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar) && !keyEvent.Control && !keyEvent.Alt;
                if (printable && _values[_field].Length < MaxFieldLength)
                {
                    _values[_field] += keyEvent.KeyChar;
                    _error = null;
                }

                break;
        }

        StateHasChanged();
        return true;
    }

    /// <summary>The filter narrows the list with every key typed, and Enter keeps it while the list is walked.</summary>
    private bool HandleFilterKey(KeyEvent keyEvent)
    {
        if (PasteHelper.IsDeleteWordKey(keyEvent))
        {
            _filter = PasteHelper.DeleteLastWord(_filter);
            StateHasChanged();
            return true;
        }

        if (PasteHelper.IsPasteKey(keyEvent))
        {
            PasteHelper.Paste(text =>
            {
                var joined = _filter + PasteHelper.SingleLine(text);
                _filter = joined.Length > MaxFieldLength ? joined[..MaxFieldLength] : joined;
            }, StateHasChanged);
            return true;
        }

        if (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.U)
        {
            _filter = string.Empty;
            StateHasChanged();
            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
                _filter = string.Empty;
                _mode = Mode.Browse;
                break;
            case ConsoleKey.Enter:
                _mode = Mode.Browse;
                break;
            case ConsoleKey.UpArrow:
                MoveSelection(-1);
                break;
            case ConsoleKey.DownArrow:
                MoveSelection(1);
                break;
            case ConsoleKey.Backspace when _filter.Length > 0:
                _filter = _filter[..^1];
                break;
            default:
                var printable = keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar) && !keyEvent.Control && !keyEvent.Alt;
                if (printable && _filter.Length < MaxFieldLength)
                {
                    _filter += keyEvent.KeyChar;
                }

                break;
        }

        StateHasChanged();
        return true;
    }

    /// <summary>The wheel moves the highlight three stations and a click highlights the station under the pointer.</summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (_mode is Mode.Browse or Mode.Filter)
        {
            if (mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
            {
                return MoveSelection(mouseEvent.WheelDelta > 0 ? -3 : 3);
            }

            if (mouseEvent.LeftClicked && StationAt(mouseEvent.Position) is { } station)
            {
                _selectedId = station.Id;
                StateHasChanged();
                return true;
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    /// <summary>Repaints when the playback line would show something new: a station starting or stopping, a pause, a new second, or a status message.</summary>
    public override void OnIdle()
    {
        base.OnIdle();
        var signature = $"{_player.CurrentTrack?.Path}|{_player.State}|{(long)_player.Position.TotalSeconds}|{_player.StatusMessage}";
        if (signature != _lastPlaybackSignature)
        {
            _lastPlaybackSignature = signature;
            StateHasChanged();
        }
    }

    /// <summary>The first listed station, which the list scrolls to keep the highlight in view.</summary>
    private int FirstVisibleIndex(IReadOnlyList<RadioStation> visible)
    {
        var selectedIndex = visible.ToList().FindIndex(s => s.Id == Selected?.Id);
        return selectedIndex >= ListRows ? selectedIndex - ListRows + 1 : 0;
    }

    private RadioStation? StationAt(Position position)
    {
        long row = position.Y - (AbsolutePosition.Y + 2);
        long column = position.X - (AbsolutePosition.X + 2);
        if (row < 0 || row >= ListRows || column < 0 || column >= ViewSize.Width - 4)
        {
            return null;
        }

        var visible = Visible();
        int index = FirstVisibleIndex(visible) + (int)row;
        return index < visible.Count ? visible[index] : null;
    }

    /// <summary>Starts the highlighted station. It replaces the music queue, so the station is the only track and Next has nothing to step to.</summary>
    private void PlaySelected()
    {
        if (Selected is not { } station)
        {
            return;
        }

        _player.ReplacePlaylist([new MusicTrack(station.Url) { Title = station.Name }]);
        _player.PlayIndex(0);
        _store.RecordPlayed(station.Id);
        _notice = null;
        StateHasChanged();
    }

    /// <summary>S: stops the station on air. The stop is confirmed on the message row, because the playback line only
    /// changes once the pump has let go of the stream.</summary>
    private void StopPlayback()
    {
        var wasPlaying = _player.State != PlaybackState.Stopped;
        _player.Stop();
        _notice = UiText.Get(wasPlaying ? "Radio.Stopped" : "Radio.NothingToStop", wasPlaying ? "Stopped" : "Nothing is playing");
        StateHasChanged();
    }

    /// <summary>Space: starts the highlighted station, or pauses and resumes it when it is the one on air.</summary>
    private void TogglePlayback()
    {
        if (Selected is not { } station)
        {
            return;
        }

        if (!IsOnAir(station))
        {
            PlaySelected();
            return;
        }

        if (_player.State == PlaybackState.Playing)
        {
            _player.Pause();
        }
        else
        {
            _player.Resume();
        }

        StateHasChanged();
    }

    private void ToggleFavourite()
    {
        if (Selected is not { } station)
        {
            return;
        }

        var marked = station with { IsFavourite = !station.IsFavourite };
        _store.Update(marked);
        _notice = string.Format(
            marked.IsFavourite
                ? UiText.Get("Radio.FavouriteAdded", "{0} is a favourite")
                : UiText.Get("Radio.FavouriteRemoved", "{0} is no longer a favourite"),
            station.Name);
        StateHasChanged();
    }

    /// <summary>Shows <paramref name="target"/>, or the whole list when it is already showing.</summary>
    private void ToggleView(ListView target)
    {
        _view = _view == target ? ListView.All : target;
        _notice = null;
        StateHasChanged();
    }

    /// <summary>Opens the form on <paramref name="station"/>, or empty for a new one. A change with nothing highlighted does nothing.</summary>
    private void OpenForm(RadioStation? station)
    {
        _editing = station;
        _values[NameField] = station?.Name ?? string.Empty;
        _values[GenreField] = station?.Genre ?? string.Empty;
        _values[UrlField] = station?.Url ?? string.Empty;
        _field = NameField;
        _error = null;
        _mode = Mode.Form;
        StateHasChanged();
    }

    private void CommitForm()
    {
        var id = _editing?.Id ?? Guid.NewGuid();
        var station = new RadioStation(id, _values[NameField], _values[UrlField], _values[GenreField], _editing?.IsFavourite ?? false).Trimmed();
        if (!station.Validate(out var error))
        {
            _error = error;
            _field = string.IsNullOrWhiteSpace(station.Name) ? NameField : UrlField;
            return;
        }

        if (_store.HasUrl(station.Url, id))
        {
            _error = UiText.Get("Radio.Duplicate", "That stream address is already in the list");
            _field = UrlField;
            return;
        }

        if (_editing is not null)
        {
            _store.Update(station);
        }
        else
        {
            _store.Add(station);
        }

        // A new station the filter or view would hide is shown anyway: the person just typed it and expects to see it.
        // A new station has never been played, so the recent list would hide it too.
        if (!station.Matches(_filter) || _view == ListView.Recent || (_view == ListView.Favourites && !station.IsFavourite))
        {
            _filter = string.Empty;
            _view = ListView.All;
        }

        _selectedId = id;
        _editing = null;
        _error = null;
        _mode = Mode.Browse;
    }

    private void AskToDelete()
    {
        if (Selected is not null)
        {
            _mode = Mode.ConfirmDelete;
            StateHasChanged();
        }
    }

    /// <summary>Removes the highlighted station and moves the highlight to the one that took its row. A station on air keeps playing until it is stopped.</summary>
    private void DeleteSelected()
    {
        var visible = Visible();
        var station = Selected;
        if (station is null)
        {
            return;
        }

        var index = visible.ToList().FindIndex(s => s.Id == station.Id);
        _store.Remove(station.Id);
        var remaining = Visible();
        _selectedId = (remaining.ElementAtOrDefault(index) ?? remaining.ElementAtOrDefault(index - 1))?.Id;
        _notice = null;
        StateHasChanged();
    }

    private bool MoveSelection(int delta)
    {
        var index = Visible().ToList().FindIndex(s => s.Id == Selected?.Id);
        return SelectAt(index + delta);
    }

    private bool SelectAt(int index)
    {
        var visible = Visible();
        if (visible.Count == 0)
        {
            return true;
        }

        _selectedId = visible[Math.Clamp(index, 0, visible.Count - 1)].Id;
        StateHasChanged();
        return true;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 0)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        for (var row = 0; row < ih; row++)
        {
            Put(buffer, 0, row, iw, string.Empty, colorScheme.Foreground, colorScheme.Background);
        }

        DrawHeader(buffer, iw, colorScheme);
        DrawRows(buffer, iw, colorScheme);
        DrawDetails(buffer, iw, colorScheme);
        DrawMessage(buffer, iw, colorScheme);
        DrawPlayback(buffer, iw, ih - 2, colorScheme);
        Put(buffer, 1, ih - 1, iw - 2, FooterText(), colorScheme.Foreground, colorScheme.Background);
    }

    private string FooterText() => _mode switch
    {
        Mode.Form => UiText.Get("Radio.FooterForm", "Enter next field  Tab/Shift+Tab move  Esc discard"),
        Mode.Filter => UiText.Get("Radio.FooterFilter", "Type to filter  Enter keep  Esc clear"),
        _ when _player.State != PlaybackState.Stopped => UiText.Get("Radio.FooterPlaying", "Enter play  Space pause  S stop  F favourite  / find  Esc close"),
        _ => UiText.Get("Radio.FooterBrowse", "Enter play  Space pause  F favourite  / find  Esc close"),
    };

    /// <summary>Column widths: the name takes what the genre column leaves, and the genre is dropped on a narrow dialog.</summary>
    private static (int Name, int Genre) Columns(int innerWidth)
    {
        var rest = innerWidth - MarkerWidth;
        return rest >= 30 + GenreColumn
            ? (rest - GenreColumn, GenreColumn)
            : (rest, 0);
    }

    private void DrawHeader(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        var total = _store.Count;
        var shown = Visible().Count;
        var count = _filter.Length > 0 || _view != ListView.All
            ? string.Format(UiText.Get("Radio.CountFiltered", "{0} of {1} stations"), shown, total)
            : string.Format(UiText.Get("Radio.Count", "{0} stations"), total);
        if (_view != ListView.All)
        {
            var viewName = _view == ListView.Recent
                ? UiText.Get("Radio.Recent", "Recent")
                : UiText.Get("Radio.Favourites", "Favourites");
            count = $"{viewName}  {count}";
        }

        // The column labels are quieter than the stations under them, so the list reads as the content.
        var (nameWidth, genreWidth) = Columns(iw - 2);
        var header = new string(' ', MarkerWidth) + Fit(UiText.Get("Radio.ColName", "Name"), nameWidth)
            + (genreWidth > 0 ? UiText.Get("Radio.ColGenre", "Genre") : string.Empty);
        Put(buffer, 1, 0, iw - 2, header, colorScheme.DisabledForeground, colorScheme.Background);

        if (count.Length < iw - header.Length - 3)
        {
            Put(buffer, iw - count.Length - 2, 0, count.Length, count, colorScheme.DisabledForeground, colorScheme.Background);
        }
    }

    private void DrawRows(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        var visible = Visible();
        var selectedId = Selected?.Id;
        if (visible.Count == 0)
        {
            var empty = _filter.Length > 0
                ? string.Format(UiText.Get("Radio.NoMatch", "No station matches \"{0}\"."), _filter)
                : _view switch
                {
                    ListView.Favourites => UiText.Get("Radio.NoFavourites", "No favourite stations. Press F on a station to mark it."),
                    ListView.Recent => UiText.Get("Radio.NoRecent", "Nothing played yet. Press Enter on a station to play it."),
                    _ => UiText.Get("Radio.Empty", "No stations yet. Press N to add one."),
                };
            Put(buffer, 1, 1, iw - 2, empty, colorScheme.DisabledForeground, colorScheme.Background);
            return;
        }

        var (nameWidth, genreWidth) = Columns(iw - 2);
        var first = FirstVisibleIndex(visible);
        for (var i = 0; i < ListRows; i++)
        {
            var y = 1 + i;
            var index = first + i;
            if (index >= visible.Count)
            {
                Put(buffer, 1, y, iw - 2, string.Empty, colorScheme.Foreground, colorScheme.Background);
                continue;
            }

            var station = visible[index];
            var highlighted = station.Id == selectedId;
            var onAir = IsOnAir(station);
            // The station on air is green even when the highlight is elsewhere, and its arrow stays with it.
            var foreground = colorScheme.Foreground;
            if (highlighted) foreground = colorScheme.SelectedFocusedForeground;
            else if (onAir) foreground = Color.Green;
            var background = highlighted ? colorScheme.SelectedFocusedBackground : colorScheme.Background;
            var marker = (station.IsFavourite ? "★" : " ")
                + (onAir ? "▶" : " ")
                + " "
                + RadioGenre.IconFor(station.Genre)
                + " ";
            var line = marker + Fit(station.Name, nameWidth) + (genreWidth > 0 ? Fit(station.Genre, genreWidth) : string.Empty);
            Put(buffer, 1, y, iw - 2, line, foreground, background);
        }
    }

    /// <summary>The highlighted station's address and genre, or the three fields while the form is open.</summary>
    private void DrawDetails(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        if (_mode == Mode.Form)
        {
            string[] labels =
            [
                UiText.Get("Radio.FieldName", "Name"),
                UiText.Get("Radio.FieldGenre", "Genre"),
                UiText.Get("Radio.FieldUrl", "Stream"),
            ];
            for (var i = 0; i < FieldCount; i++)
            {
                var active = i == _field;
                Put(buffer, 1, DetailsTop + i, LabelWidth, Fit(labels[i] + ":", LabelWidth), colorScheme.DisabledForeground, colorScheme.Background);
                Put(buffer, 1 + LabelWidth, DetailsTop + i, iw - 2 - LabelWidth,
                    _values[i] + (active ? "_" : string.Empty),
                    active ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground, colorScheme.Background);
            }

            return;
        }

        var station = Selected;
        Put(buffer, 1, DetailsTop, iw - 2, station?.Url ?? string.Empty, colorScheme.Foreground, colorScheme.Background);

        // The favourite label comes first and in yellow, so a favourite is spotted without reading the genre.
        var x = 1;
        if (station?.IsFavourite == true)
        {
            var favourite = "★ " + UiText.Get("Radio.Favourite", "Favourite") + "   ";
            Put(buffer, x, DetailsTop + 1, iw - 2 - x, favourite, Color.Yellow, colorScheme.Background);
            x += favourite.Length;
        }

        var genre = string.IsNullOrWhiteSpace(station?.Genre)
            ? string.Empty
            : RadioGenre.IconFor(station.Genre) + " " + string.Format(UiText.Get("Radio.GenreLine", "Genre: {0}"), station.Genre);
        Put(buffer, x, DetailsTop + 1, iw - 2 - x, genre, colorScheme.DisabledForeground, colorScheme.Background);
    }

    private void DrawMessage(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        var y = MessageRow;
        if (_mode == Mode.ConfirmDelete && Selected is { } doomed)
        {
            Put(buffer, 1, y, iw - 2, string.Format(UiText.Get("Prompt.DeleteNamed", "Delete \"{0}\"? (y/n)"), doomed.Name), colorScheme.Error, colorScheme.Background);
        }
        else if (_mode == Mode.Form)
        {
            Put(buffer, 1, y, iw - 2,
                _error ?? (_editing is null
                    ? UiText.Get("Radio.FormNew", "New station: a name and a stream address http:// or https://")
                    : UiText.Get("Radio.FormChange", "Change station: a name and a stream address http:// or https://")),
                _error is null ? colorScheme.DisabledForeground : colorScheme.Error, colorScheme.Background);
        }
        else if (_mode == Mode.Filter)
        {
            Put(buffer, 1, y, iw - 2, string.Format(UiText.Get("Radio.FilterInput", "Filter: {0}_"), _filter), colorScheme.SelectedFocusedForeground, colorScheme.Background);
        }
        else if (_notice is not null)
        {
            Put(buffer, 1, y, iw - 2, _notice, colorScheme.DisabledForeground, colorScheme.Background);
        }
        else if (_filter.Length > 0)
        {
            Put(buffer, 1, y, iw - 2, string.Format(UiText.Get("Radio.FilterHint", "Filter: {0}  (Esc shows every station)"), _filter), colorScheme.DisabledForeground, colorScheme.Background);
        }
    }

    /// <summary>
    /// What the player is doing, on the line above the footer: the station on air with a state mark (a green dot
    /// while it plays, a pause bar when it is paused), its genre icon, its name and the elapsed time. The player's
    /// error replaces it when the station could not start.
    /// </summary>
    private void DrawPlayback(IBuffer buffer, int iw, int y, ColorScheme colorScheme)
    {
        var track = _player.CurrentTrack;
        if (_player.StatusIsError && !string.IsNullOrEmpty(_player.StatusMessage))
        {
            Put(buffer, 1, y, iw - 2, _player.StatusMessage, colorScheme.Error, colorScheme.Background);
            return;
        }

        if (track is null || _player.State == PlaybackState.Stopped)
        {
            Put(buffer, 1, y, iw - 2, UiText.Get("Radio.NotPlaying", "Not playing"), colorScheme.DisabledForeground, colorScheme.Background);
            return;
        }

        var playing = _player.State == PlaybackState.Playing;
        var state = playing
            ? "● " + UiText.Get("Radio.OnAir", "On air")
            : "‖ " + UiText.Get("Radio.Paused", "Paused");
        Put(buffer, 1, y, iw - 2, state, playing ? Color.Green : colorScheme.DisabledForeground, colorScheme.Background);

        var x = 1 + state.Length + 3;
        var station = _store.Find().FirstOrDefault(s => string.Equals(s.Url, track.Path, StringComparison.Ordinal));
        var icon = station is null ? string.Empty : RadioGenre.IconFor(station.Genre) + " ";
        var rest = $"{icon}{track.DisplayTitle}  {FormatElapsed(_player.Position)}";
        Put(buffer, x, y, iw - 2 - x, rest, colorScheme.Foreground, colorScheme.Background);
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"mm\:ss");

    /// <summary>Clips to <paramref name="width"/> - 1 and pads to <paramref name="width"/>, so neighbouring columns never touch.</summary>
    private static string Fit(string text, int width) =>
        (text.Length > width - 1 ? text[..(width - 1)] : text).PadRight(width);

    private void Put(IBuffer buffer, int x, int y, int width, string text, Color foreground, Color background)
    {
        if (width <= 0)
        {
            return;
        }

        var clipped = text.Length > width ? text[..width] : text.PadRight(width);
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1 + x, AbsolutePosition.Y + 1 + y),
            new Size(width, 1),
            new ConxelString(clipped, foreground, background));
    }

    #region Test seams

    internal bool IsFormOpenForTest => _mode == Mode.Form;
    internal bool ConfirmingDeleteForTest => _mode == Mode.ConfirmDelete;
    internal bool IsFilteringForTest => _mode == Mode.Filter;
    internal bool FavouritesOnlyForTest => _view == ListView.Favourites;
    internal bool RecentOnlyForTest => _view == ListView.Recent;
    internal string? ErrorForTest => _error;
    internal string FilterForTest => _filter;
    internal RadioStation? SelectedForTest => Selected;

    #endregion
}

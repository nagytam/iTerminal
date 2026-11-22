using iTerminal.Core.Dialogs;
using System.Globalization;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Controls.Charts;
using iConsole.Data;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Widgets.Services.Weather;

namespace iTerminal.Widgets.Dialogs.Weather;

/// <summary>
/// Tools > Weather: the saved places on the left, and on the right the place under the cursor — its conditions now, then
/// either the next 24 hours (a temperature line and the rain under it) or the next 7 days (a table). A search finds a place
/// by name and saves the one chosen.
///
/// <para>The dialog never waits on the network. A forecast is fetched on a background task and read back in <c>OnIdle</c>;
/// the cursor's place is refreshed from <c>OnIdle</c> when its forecast is older than <see cref="ForecastService.MaxAge"/>.
/// Searches land only if they are the newest one.</para>
/// </summary>
public class WeatherDialog : SplitPaneDialog
{
    private enum Focus { Places, Search, Hits }

    private enum View { Hourly, Daily }

    internal sealed record PlaceRow(SavedPlace Place, bool IsHome);

    private const int MinLeftWidth = 22;
    private const int MaxLeftWidth = 34;
    private const int DayWidth = 6;
    private const int NumberWidth = 6;
    private const int RainWidth = 7;
    private const int ChanceWidth = 7;
    private static readonly char[] RainLevels = ['▁', '▂', '▃', '▄', '▅', '▆', '▇', '█'];

    private readonly IForecastService _forecasts;
    private readonly TimeProvider _time;
    private readonly ConcurrentObservableCollection<PlaceRow> _placeItems = new();
    private readonly ConcurrentObservableCollection<PlaceHit> _hitItems = new();
    private readonly PlaceListBox _placeListBox;
    private readonly HitListBox _hitListBox;
    private readonly TextBox _inputBox;

    private readonly Lock _parkLock = new();
    private string? _parkedMessage;
    private (string Query, IReadOnlyList<PlaceHit> Hits)? _parkedHits;
    private volatile bool _placesChanged;
    private volatile bool _forecastBusy;
    private volatile bool _refreshAgain;
    private int _searchGeneration; // guarded by _parkLock, with the parked results, so a result cannot land after a newer search began
    private Focus _focus = Focus.Places;
    private View _view = View.Hourly;
    private string _message = string.Empty;
    private string? _selectKey;
    private string _confirmPrompt = string.Empty;
    private Action? _confirmAction;

    public WeatherDialog(IForecastService forecasts, IBeepService beepService, IDateTimeProviderService dateTimeProviderService, IThemeService themeService, TimeProvider? time = null)
        : base(nameof(WeatherDialog), themeService)
    {
        _forecasts = forecasts;
        _time = time ?? TimeProvider.System;
        Title = UiText.Get("Dialog.Weather", "Weather");

        _placeListBox = new PlaceListBox(new ListBoxModel<PlaceRow>(_placeItems), themeService)
        {
            EmptyText = UiText.Get("Weather.Empty", " No saved places — press a to find one."),
        };
        _hitListBox = new HitListBox(new ListBoxModel<PlaceHit>(_hitItems), themeService)
        {
            EmptyText = UiText.Get("Weather.NoHits", " Nothing found."),
        };
        _inputBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            IsVisible = false,
            IsEnabled = false,
            CanFocus = false,
        };

        AddControl(_placeListBox);
        AddControl(_hitListBox);
        AddControl(_inputBox);
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        _forecasts.Changed += OnPlacesChanged;
        RebuildPlaces();
        if (_forecasts.Places.Count == 0)
        {
            BeginSearch();
        }
    }

    public override void Close()
    {
        _forecasts.Changed -= OnPlacesChanged;
        _confirmAction = null;
        base.Close();
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        SetFocusTo(_focus);
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize);
        }

        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        if (iw > 0 && ih > 0)
        {
            // Dialog rows: the header is row 1, the section headings row 2, the lists start at row 3 and end above the query
            // box (row ih - 1), which is above the status line (row ih).
            var leftWidth = LeftWidth(iw);
            var listHeight = Math.Max(0, ih - 4);
            _placeListBox.Position = new Position(1, 3);
            _placeListBox.ViewSize = new Size(leftWidth, listHeight);
            _hitListBox.Position = new Position(1, 3);
            _hitListBox.ViewSize = new Size(leftWidth, listHeight);
            _inputBox.Position = new Position(1, ih - 1);
            _inputBox.ViewSize = new Size(leftWidth, 1);
            _inputBox.ScrollableModel.ViewSize = _inputBox.ViewSize;
        }

        base.OnInitializeLayout();
        _placeListBox.UpdateModel();
        _hitListBox.UpdateModel();
    }

    /// <summary>The left pane's width: a third of the dialog, kept between the minimum and the maximum, and never more than half of it.</summary>
    internal static int LeftWidth(int innerWidth) => Math.Min(Math.Clamp(innerWidth / 3, MinLeftWidth, MaxLeftWidth), innerWidth / 2);

    #region Places

    private void OnPlacesChanged(object? sender, EventArgs e) => _placesChanged = true;

    /// <summary>Whether the saved places or the home place differ from the rows the list shows.</summary>
    private bool PlacesMoved()
    {
        var homeKey = _forecasts.Home?.Key;
        var shown = _placeItems.Select(row => (row.Place.Key, row.IsHome)).ToList();
        return !shown.SequenceEqual(_forecasts.Places.Select(place => (place.Key, IsHome: place.Key == homeKey)));
    }

    /// <summary>Re-reads the saved places and the home place from memory, and keeps the cursor on the same place.</summary>
    private void RebuildPlaces()
    {
        var places = _forecasts.Places;
        var homeKey = _forecasts.Home?.Key;
        var cursorKey = _selectKey ?? CursorPlace()?.Key;
        _selectKey = null;
        var previousRow = (int)_placeListBox.ScrollableModel.CursorPosition.Y;
        _placeItems.ReplaceAll([.. places.Select(place => new PlaceRow(place, place.Key == homeKey))]);
        _placeListBox.UpdateModel();

        // The cursor stays on its place; with none to keep (the first open, or the one kept was removed) it goes to the home place.
        var list = places.ToList();
        var index = cursorKey is null ? -1 : list.FindIndex(place => place.Key == cursorKey);
        if (index < 0 && cursorKey is not null)
        {
            // The place under the cursor is gone: the cursor stays on its row, so repeated removals walk down the list.
            index = Math.Min(previousRow, list.Count - 1);
        }
        else if (index < 0 && homeKey is not null)
        {
            index = list.FindIndex(place => place.Key == homeKey);
        }

        MoveCursor(_placeListBox.ScrollableModel, Math.Max(0, index), list.Count);
        _placeListBox.UpdateModel();
        StateHasChanged();
    }

    /// <summary>The place under the cursor in the places list, or null when there is none.</summary>
    private SavedPlace? CursorPlace() => CursorItem(_placeListBox)?.Place;

    private static void MoveCursor(IScrollableModel scrollableModel, int index, int count)
    {
        var clamped = count <= 0 ? 0 : Math.Clamp(index, 0, count - 1);
        scrollableModel.CursorPosition.Y = clamped;
        scrollableModel.ScrollPosition.Y = Math.Min(scrollableModel.ScrollPosition.Y, clamped);
    }

    private static T? CursorItem<T>(ListBox<T> box) where T : class
    {
        var items = box.ListModel.ListItems;
        var index = (int)box.ScrollableModel.CursorPosition.Y;
        return index >= 0 && index < items.Count ? items[index].Item : null;
    }

    /// <summary>Fetches the forecast of the place under the cursor when it is missing or old. Runs on a background task.</summary>
    private void RefreshCursorPlace()
    {
        if (_forecastBusy)
        {
            // A request during a download is not lost: the cursor's place is fetched again once this one has landed.
            _refreshAgain = true;
            return;
        }

        if (CursorPlace() is not { } place)
        {
            return;
        }

        _refreshAgain = false;

        _forecastBusy = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await _forecasts.RefreshAsync(place);
                Park(null);
            }
            catch (Exception exception)
            {
                Park(string.Format(UiText.Get("Weather.Offline", "Could not reach the forecast service ({0}). Showing the last forecast kept."), exception.Message));
            }
            finally
            {
                _forecastBusy = false;
            }
        });
    }

    private void RemoveCursorPlace()
    {
        if (CursorPlace() is not { } place)
        {
            return;
        }

        _confirmPrompt = string.Format(UiText.Get("Weather.ConfirmRemove", "Remove {0}?  y:yes  any other key:no"), place.Name);
        _confirmAction = () =>
        {
            var removed = _forecasts.RemovePlace(place);
            SetMessage(removed
                ? string.Format(UiText.Get("Weather.Removed", "Removed {0}"), place.Name)
                : string.Format(UiText.Get("Weather.NotSaved", "{0} is not saved"), place.Name));
        };
        StateHasChanged();
    }

    private void SetHomeFromCursor()
    {
        if (CursorPlace() is not { } place)
        {
            return;
        }

        _forecasts.SetHome(place);
        SetMessage(string.Format(UiText.Get("Weather.HomeSet", "{0} is the home place"), place.Name));
    }

    #endregion

    #region Search

    private void BeginSearch()
    {
        // Whatever the last search finds from now on is for a query that is gone.
        lock (_parkLock)
        {
            _searchGeneration++;
            _parkedHits = null;
            _parkedMessage = null;
        }
        _inputBox.Label = UiText.Get("Weather.FindPrompt", "Find a place by name");
        _inputBox.Text = string.Empty;
        _hitItems.ReplaceAll([]);
        _hitListBox.UpdateModel();
        SetMessage(string.Empty);
        SetFocusTo(Focus.Search);
    }

    /// <summary>Enter in the query box: searches for what is typed. The search runs on a background task; only the newest may land.</summary>
    private void SubmitSearch()
    {
        var query = _inputBox.Text.Trim();
        if (query.Length == 0)
        {
            return;
        }

        // A new search supersedes one still running: only the newest may land, so the older one's hits never replace this one's.
        int generation;
        lock (_parkLock)
        {
            generation = ++_searchGeneration;
            _parkedHits = null;
            _parkedMessage = null;
        }

        SetMessage(UiText.Get("Msg.Searching", "Searching…"));
        _ = Task.Run(async () =>
        {
            try
            {
                var hits = await _forecasts.SearchAsync(query);
                ParkHits(generation, query, hits);
            }
            catch (Exception exception)
            {
                ParkSearchFailure(generation, string.Format(UiText.Get("Msg.SearchFailed", "Error: search failed — {0}"), exception.Message));
            }
        });
    }

    private void ShowHits(string query, IReadOnlyList<PlaceHit> hits)
    {
        _hitItems.ReplaceAll([.. hits]);
        _hitListBox.UpdateModel();
        MoveCursor(_hitListBox.ScrollableModel, 0, hits.Count);
        if (hits.Count == 0)
        {
            SetMessage(string.Format(UiText.Get("Weather.NothingFound", "No place found for \"{0}\"."), query));
            return;
        }

        SetMessage(string.Format(UiText.Get("Weather.Found", "{0} found — ↑↓ choose, Enter saves the chosen one"), hits.Count));
        // The hits take the focus only while the query box has it: the user may have moved on while the search ran.
        if (_focus == Focus.Search)
        {
            SetFocusTo(Focus.Hits);
        }
    }

    /// <summary>Enter on a hit: saves it. Focus goes back to the places, and the dialog stays open.</summary>
    private void SaveChosenHit()
    {
        if (CursorItem(_hitListBox) is not { } hit)
        {
            return;
        }

        var place = SavedPlace.From(hit);
        var added = _forecasts.AddPlace(place);
        if (added)
        {
            _selectKey = place.Key;
        }
        SetMessage(added
            ? string.Format(UiText.Get("Weather.Saved", "Saved {0}"), place.Name)
            : string.Format(UiText.Get("Weather.AlreadySaved", "{0} is already saved"), place.Name));
        SetFocusTo(Focus.Places);
    }

    #endregion

    #region Focus and parked results

    /// <summary>Puts the focus on one of the three panes and shows the controls that pane needs. The query box is shown whenever the search is.</summary>
    private void SetFocusTo(Focus focus)
    {
        _focus = focus;
        var searching = focus != Focus.Places;
        _placeListBox.IsVisible = !searching;
        _hitListBox.IsVisible = searching;
        _inputBox.CanFocus = searching;
        _inputBox.IsVisible = searching;
        _inputBox.IsEnabled = searching;
        _inputBox.ResetFocus();
        switch (focus)
        {
            case Focus.Places:
                _placeListBox.SetFocus();
                break;
            case Focus.Search:
                _inputBox.SetFocus();
                break;
            case Focus.Hits:
                _hitListBox.SetFocus();
                break;
        }

        StateHasChanged();
    }

    private void SetMessage(string message)
    {
        _message = message;
        StateHasChanged();
    }

    private void Park(string? message)
    {
        lock (_parkLock)
        {
            if (message is not null)
            {
                _parkedMessage = message;
            }
            else
            {
                _parkedMessage ??= UiText.Get("Weather.Updated", "Forecast updated");
            }
        }
    }

    /// <summary>Parks a search's hits, but only when that search is still the newest: checked under the lock, as a new search is started under it.</summary>
    private void ParkHits(int generation, string query, IReadOnlyList<PlaceHit> hits)
    {
        lock (_parkLock)
        {
            if (generation == _searchGeneration)
            {
                _parkedHits = (query, hits);
            }
        }
    }

    private void ParkSearchFailure(int generation, string message)
    {
        lock (_parkLock)
        {
            if (generation == _searchGeneration)
            {
                _parkedMessage = message;
            }
        }
    }

    /// <summary>Applies what the background work parked, rebuilds the list when the places changed, and asks for a forecast when the cursor's is old.</summary>
    public override void OnIdle()
    {
        base.OnIdle();

        string? message;
        (string Query, IReadOnlyList<PlaceHit> Hits)? hits;
        lock (_parkLock)
        {
            message = _parkedMessage;
            hits = _parkedHits;
            _parkedMessage = null;
            _parkedHits = null;
        }

        if (hits is { } found)
        {
            ShowHits(found.Query, found.Hits);
        }

        if (message is not null)
        {
            SetMessage(message);
        }

        if (_placesChanged)
        {
            _placesChanged = false;
            // A forecast landing also raises the change: the list is rebuilt only when the places or the home place moved.
            if (PlacesMoved())
            {
                RebuildPlaces();
            }
            else
            {
                StateHasChanged();
            }
        }

        if (!_forecastBusy && CursorPlace() is { } place && (_refreshAgain || _forecasts.NeedsRefresh(place)))
        {
            RefreshCursorPlace();
        }
    }

    #endregion

    #region Input

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_confirmAction is not null)
        {
            yield return ("Y", UiText.Get("Common.Confirm", "Confirm"));
            yield return ("Any other key", UiText.Get("Common.Cancel", "Cancel"));
            yield break;
        }

        switch (_focus)
        {
            case Focus.Places:
                yield return ("A", UiText.Get("DialogHint.FindPlace", "Find a place"));
                yield return ("D/Delete", UiText.Get("DialogHint.RemovePlace", "Remove the place"));
                yield return ("H", UiText.Get("DialogHint.HomePlace", "Set as home"));
                yield return ("V", UiText.Get("DialogHint.HourlyDaily", "Hourly or daily"));
                yield return ("F5", UiText.Get("DialogHint.RefreshForecast", "Refresh the forecast"));
                break;
            case Focus.Search:
                yield return ("Enter", UiText.Get("DialogHint.SearchPlace", "Search"));
                yield return ("Up/Down", UiText.Get("DialogHint.ChooseHit", "Choose a place"));
                yield return ("Esc", UiText.Get("DialogHint.BackToList", "Back to the list"));
                break;
            case Focus.Hits:
                yield return ("Enter", UiText.Get("DialogHint.SavePlace", "Save the chosen place"));
                yield return ("Up/Down", UiText.Get("DialogHint.ChooseHit", "Choose a place"));
                yield return ("Esc", UiText.Get("DialogHint.BackToSearch", "Back to the search"));
                break;
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_confirmAction is not null)
        {
            return HandleConfirmKey(keyEvent);
        }

        switch (_focus)
        {
            case Focus.Places:
                return await HandlePlacesKeyAsync(keyEvent);
            case Focus.Search:
                return await HandleSearchKeyAsync(keyEvent);
            default:
                return await HandleHitsKeyAsync(keyEvent);
        }
    }

    private async Task<bool> HandlePlacesKeyAsync(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Delete)
        {
            RemoveCursorPlace();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.F5)
        {
            RefreshCursorPlace();
            return true;
        }

        // Only a plain letter is a shortcut: with Alt or Ctrl it is a chord the dialog does not own.
        if (keyEvent.Alt || keyEvent.Control)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        switch (char.ToLowerInvariant(keyEvent.KeyChar))
        {
            case 'a':
                BeginSearch();
                return true;
            case 'd':
                RemoveCursorPlace();
                return true;
            case 'h':
                SetHomeFromCursor();
                return true;
            case 'v':
                _view = _view == View.Hourly ? View.Daily : View.Hourly;
                StateHasChanged();
                return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private async Task<bool> HandleSearchKeyAsync(KeyEvent keyEvent)
    {
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
                // With no saved place the search is the only pane, so Esc closes the dialog instead of stepping back to an empty list.
                if (_forecasts.Places.Count == 0)
                {
                    return await base.HandleKeyEventAsync(keyEvent);
                }

                SetFocusTo(Focus.Places);
                return true;
            case ConsoleKey.Enter:
                SubmitSearch();
                return true;
            case ConsoleKey.UpArrow:
            case ConsoleKey.DownArrow:
            case ConsoleKey.PageUp:
            case ConsoleKey.PageDown:
            case ConsoleKey.Home:
            case ConsoleKey.End:
                // The query box keeps the keyboard so typing always searches; the hits are driven from here.
                return await _hitListBox.HandleKeyEventAsync(keyEvent);
            case ConsoleKey.Tab:
                // The query box and the hit list are the two panes that are up together: Tab moves between them.
                SetFocusTo(Focus.Hits);
                return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private async Task<bool> HandleHitsKeyAsync(KeyEvent keyEvent)
    {
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
                SetFocusTo(Focus.Search);
                return true;
            case ConsoleKey.Enter:
                SaveChosenHit();
                return true;
            case ConsoleKey.Tab:
                SetFocusTo(Focus.Search);
                return true;
        }

        if (!keyEvent.Alt && !keyEvent.Control && char.ToLowerInvariant(keyEvent.KeyChar) == 'a')
        {
            BeginSearch();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private bool HandleConfirmKey(KeyEvent keyEvent)
    {
        var action = _confirmAction;
        _confirmAction = null;
        _confirmPrompt = string.Empty;
        if (char.ToLowerInvariant(keyEvent.KeyChar) == 'y')
        {
            action?.Invoke();
        }
        else
        {
            SetMessage(string.Empty);
        }

        return true;
    }

    #endregion

    #region Draw

    internal static string KeyHints => UiText.Get("Hints.Weather", "a:find  d/Del:remove  h:home  v:hourly/daily  F5:refresh  Esc:close");
    internal static string SearchKeyHints => UiText.Get("Hints.WeatherSearch", "Enter:search  ↑↓:choose  Esc:back");
    internal static string HitKeyHints => UiText.Get("Hints.WeatherHits", "Enter:save  ↑↓:choose  a:find  Esc:back");

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        var ix = (int)AbsolutePosition.X + 1;
        var iy = (int)AbsolutePosition.Y + 1;
        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 0)
        {
            return;
        }

        var leftWidth = LeftWidth(iw);
        var rightX = ix + leftWidth + 2;
        var rightWidth = iw - leftWidth - 2;
        var shown = CursorPlace();

        WriteLine(buffer, ix, iy, iw, HeaderText(shown), colorScheme.Foreground, colorScheme.Background);
        var leftFocused = _focus == Focus.Places ? _placeListBox.IsFocused : _hitListBox.IsFocused;
        DrawSectionHeader(buffer, ix, iy + 1, leftWidth, _focus == Focus.Places ? UiText.Get("Weather.Places", "Places") : UiText.Get("Weather.Found.Heading", "Found"), leftFocused, colorScheme);
        if (rightWidth > 0)
        {
            DrawSectionHeader(buffer, rightX, iy + 1, rightWidth, _view == View.Hourly ? UiText.Get("Weather.NextHours", "Next 24 hours") : UiText.Get("Weather.NextDays", "Next 7 days"), false, colorScheme);
        }

        // The detail rows run from the third dialog row to the one above the query box: ih - 4 rows, the view takes what is left after the conditions and a blank.
        // They are cleared first: the layers are not cleared between frames, so a row of an earlier view or place would stay.
        for (var row = 0; row < ih - 4 && rightWidth > 0; row++)
        {
            WriteLine(buffer, rightX, iy + 2 + row, rightWidth, string.Empty, colorScheme.Foreground, colorScheme.Background);
        }

        if (shown is not null && rightWidth > 0 && ih > 6)
        {
            var forecast = _forecasts.GetForecast(shown);
            var lines = new List<string>();
            if (forecast is null)
            {
                lines.Add(_forecastBusy ? UiText.Get("Weather.Fetching", "Fetching the forecast…") : UiText.Get("Weather.NoForecast", "No forecast yet — F5 fetches it."));
            }
            else
            {
                lines.Add(CurrentLine(forecast));
                lines.Add(string.Empty);
                lines.AddRange(ViewLines(forecast, _view, rightWidth, ih - 6));
            }

            for (var row = 0; row < lines.Count && row < ih - 4; row++)
            {
                WriteLine(buffer, rightX, iy + 2 + row, rightWidth, lines[row], colorScheme.Foreground, colorScheme.Background);
            }
        }

        var hints = _focus switch
        {
            Focus.Search => SearchKeyHints,
            Focus.Hits => HitKeyHints,
            _ => KeyHints,
        };
        var status = _confirmAction is not null ? _confirmPrompt : _message;
        DrawStatusAndHints(buffer, ix, iy + ih - 1, iw, status, colorScheme.Foreground, hints, colorScheme);
    }

    private string HeaderText(SavedPlace? place)
    {
        if (place is null)
        {
            return string.Empty;
        }

        var forecast = _forecasts.GetForecast(place);
        var updated = forecast is null
            ? string.Empty
            : " · " + (_time.GetUtcNow() - forecast.FetchedAt > ForecastService.MaxAge
                ? string.Format(UiText.Get("Weather.Updated.Old", "old, updated {0:g}"), forecast.FetchedAt.LocalDateTime)
                : string.Format(UiText.Get("Weather.Updated.At", "updated {0:HH:mm}"), forecast.FetchedAt.LocalDateTime));
        var home = place.Key == _forecasts.Home?.Key ? " ⌂" : string.Empty;
        return PlaceTitle(place) + home + updated;
    }

    /// <summary>"Name, Region, Country" for a place, leaving out an empty region.</summary>
    internal static string PlaceTitle(SavedPlace place) => PlaceTitle(place.Name, place.Region, place.Country);

    internal static string PlaceTitle(string name, string region, string country) =>
        string.Join(", ", new[] { name, region, country }.Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>The current conditions as one line: temperature, glyph and description.</summary>
    internal static string CurrentLine(Forecast forecast)
    {
        var current = forecast.Current;
        return string.Format(CultureInfo.InvariantCulture, "{0:0.#}°C  {1}  {2}", current.TemperatureC, WmoWeather.Glyph(current.WeatherCode, current.IsDay), WmoWeather.Describe(current.WeatherCode));
    }

    /// <summary>The lines of the chosen view that fit in <paramref name="height"/> rows.</summary>
    private static IReadOnlyList<string> ViewLines(Forecast forecast, View view, int width, int height) =>
        view == View.Hourly ? HourlyLines(forecast.Hourly, width, height) : DailyLines(forecast.Daily, width, height);

    /// <summary>
    /// The next hours as a temperature line (braille, with the range) above a rain row (one bar per hour) and an hour
    /// row. Hours are sampled when there are more than the width holds.
    /// </summary>
    internal static IReadOnlyList<string> HourlyLines(IReadOnlyList<HourlyPoint> hours, int width, int height)
    {
        const int LabelWidth = 6;
        var columns = Math.Min(hours.Count, width - LabelWidth);
        if (columns <= 0 || height < 3)
        {
            return [];
        }

        var sampled = Buckets(hours, columns);
        var temperatures = sampled.Select(hour => hour.TemperatureC).ToArray();
        var minimum = temperatures.Min();
        var maximum = temperatures.Max();
        var chartHeight = Math.Max(1, height - 2);
        var lines = new List<string>();
        foreach (var row in LineChart.Render(temperatures, columns, chartHeight, minimum, maximum))
        {
            lines.Add(new string(' ', LabelWidth) + row);
        }

        var peak = Math.Max(1.0, sampled.Max(hour => hour.PrecipitationMm));
        var rain = string.Concat(sampled.Select(hour => RainLevels[Math.Clamp((int)Math.Round(hour.PrecipitationMm / peak * (RainLevels.Length - 1)), 0, RainLevels.Length - 1)]));
        lines.Add(Fit(UiText.Get("Weather.Rain.Label", "Rain").PadRight(LabelWidth), LabelWidth) + rain);

        var labels = new char[columns];
        Array.Fill(labels, ' ');
        for (var column = 0; column < columns; column++)
        {
            if (sampled[column].Time.Hour % 3 == 0 && column + 2 <= columns)
            {
                var text = sampled[column].Time.ToString("HH", CultureInfo.InvariantCulture);
                text.CopyTo(0, labels, column, 2);
            }
        }

        lines.Add(new string(' ', LabelWidth) + new string(labels));
        return lines;
    }

    /// <summary>The narrowest the weather text of a day is drawn at; below it the text goes and the numbers stay.</summary>
    private const int MinDescriptionWidth = 8;

    /// <summary>
    /// The next days as a table: day, weather, lowest and highest, rain and the chance of it. What fits decides the columns: the chance
    /// goes first when the pane is narrow, then the rain, and the weather text goes before any number. The day and the extremes stay.
    /// </summary>
    internal static IReadOnlyList<string> DailyLines(IReadOnlyList<DailyPoint> days, int width, int height)
    {
        if (days.Count == 0 || height < 2 || width <= 0)
        {
            return [];
        }

        var columns = new List<DailyColumn>
        {
            new(UiText.Get("Weather.Col.Min", "Min"), NumberWidth, day => string.Format(CultureInfo.InvariantCulture, "{0:0}°", day.MinC)),
            new(UiText.Get("Weather.Col.Max", "Max"), NumberWidth, day => string.Format(CultureInfo.InvariantCulture, "{0:0}°", day.MaxC)),
            new(UiText.Get("Weather.Col.Rain", "Rain"), RainWidth, day => day.PrecipitationMm.ToString("0.0", CultureInfo.InvariantCulture)),
            new(UiText.Get("Weather.Col.Chance", "Chance"), ChanceWidth, day => $"{day.PrecipitationChanceMax}%"),
        };
        while (columns.Count > 2 && DayWidth + columns.Sum(column => column.Width) + MinDescriptionWidth > width)
        {
            columns.RemoveAt(columns.Count - 1);
        }

        var numbersWidth = columns.Sum(column => column.Width);
        var showDescription = DayWidth + numbersWidth + MinDescriptionWidth <= width;
        var descriptionWidth = showDescription ? width - DayWidth - numbersWidth : 0;
        var heading = string.Empty.PadRight(DayWidth)
            + (showDescription ? Column(UiText.Get("Weather.Col.Weather", "Weather"), descriptionWidth, right: false) : string.Empty)
            + string.Concat(columns.Select(column => Column(column.Heading, column.Width, right: true)));
        var lines = new List<string> { Fit(heading, width) };
        foreach (var day in days.Take(height - 1))
        {
            var name = day.Date.ToDateTime(TimeOnly.MinValue).ToString("ddd", CultureInfo.CurrentUICulture);
            var description = $"{WmoWeather.Glyph(day.WeatherCode)} {WmoWeather.Describe(day.WeatherCode)}";
            lines.Add(Fit(
                Column(name, DayWidth, right: false)
                + (showDescription ? Column(description, descriptionWidth, right: false) : string.Empty)
                + string.Concat(columns.Select(column => Column(column.Value(day), column.Width, right: true))),
                width));
        }

        return lines;
    }

    private sealed record DailyColumn(string Heading, int Width, Func<DailyPoint, string> Value);

    /// <summary>
    /// One column per bucket of hours when there are more hours than columns: the mean temperature, and the most rain and the highest
    /// chance in the bucket, so a shower in an hour that is not drawn still shows. The bucket is labelled with its first hour.
    /// </summary>
    private static IReadOnlyList<HourlyPoint> Buckets(IReadOnlyList<HourlyPoint> hours, int columns)
    {
        if (hours.Count <= columns)
        {
            return hours;
        }

        return Enumerable.Range(0, columns).Select(column =>
        {
            var from = column * hours.Count / columns;
            var to = (column + 1) * hours.Count / columns;
            var bucket = hours.Skip(from).Take(to - from).ToList();
            return new HourlyPoint(
                bucket[0].Time,
                bucket.Average(hour => hour.TemperatureC),
                bucket.Max(hour => hour.PrecipitationMm),
                bucket.Max(hour => hour.PrecipitationChance),
                bucket[0].WeatherCode);
        }).ToList();
    }

    /// <summary>A heading or value cut to its column and padded to it: right-aligned for numbers, left for text.</summary>
    private static string Column(string text, int width, bool right)
    {
        var cut = Fit(text, width);
        return right ? cut.PadLeft(width) : cut.PadRight(width);
    }

    private static string Fit(string text, int width) => text.Length <= width ? text : text[..Math.Max(0, width)];

    private sealed class PlaceListBox : ListBox<PlaceRow>
    {
        public PlaceListBox(IListBoxModel<PlaceRow> listModel, IThemeService themeService)
            : base(listModel, themeService)
        {
            ListModel.TransformItem = (row, _) => new ListItem<PlaceRow>(row)
            {
                Value = Fit(" " + PlaceTitle(row.Place) + (row.IsHome ? " ⌂" : string.Empty), (int)ViewSize.Width),
                IsEnabled = true,
            };
        }
    }

    private sealed class HitListBox : ListBox<PlaceHit>
    {
        public HitListBox(IListBoxModel<PlaceHit> listModel, IThemeService themeService)
            : base(listModel, themeService)
        {
            ListModel.TransformItem = (hit, _) => new ListItem<PlaceHit>(hit)
            {
                Value = Fit(" " + PlaceTitle(hit.Name, hit.Region, hit.Country), (int)ViewSize.Width),
                IsEnabled = true,
            };
        }
    }

    #endregion
}

using iTerminal.Core.Helper;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Model.Map;
using iTerminal.Core.Services.Map;
using iTerminal.Core.Services.Web;

namespace iTerminal.Core.Dialogs.Map;

/// <summary>
/// The Map: OpenStreetMap drawn as ASCII art, full screen. Water, parks, roads and buildings become characters
/// of different density and colour (see <see cref="MapAsciiRenderer"/>). It works online (missing tiles come
/// from the tile server and are saved) and offline (saved tiles and <c>.mbtiles</c> files only), pans with the
/// arrows or a click, zooms with the keys or the mouse wheel, finds places by name, and keeps saved places.
///
/// <para>The dialog only draws and routes keys. Tiles load on background tasks through <see cref="MapModel"/>;
/// <see cref="Dialog.OnIdle"/> notices that one landed and repaints, so no key handler or
/// <see cref="Draw"/> waits for the disk or the network.</para>
/// </summary>
public class MapDialog : Dialog
{
    private enum Mode
    {
        Browse,
        Search,
        List,
        Help,
    }

    private enum ListKind
    {
        Places,
        Results,
    }

    private const int ChromeRows = 2;
    private const string Attribution = "© OpenStreetMap contributors";

    private static readonly string[] HelpLines =
    [
        "Map — OpenStreetMap as ASCII art",
        "",
        "Moving",
        "  Arrows               Pan a little       Shift+Arrows   Pan half a screen",
        "  Click                Centre the map on that spot",
        "  Home                 Back to the home place (Budapest)",
        "",
        "Zoom",
        "  +  =  PgUp           Zoom in            -  PgDn        Zoom out",
        "  Mouse wheel          Zoom in or out at the pointer",
        "",
        "Finding places",
        "  /  F6  G             Search a name (needs the network) or type coordinates: 47.5, 19.04",
        "  M                    Save the centre as a place        B  F2   Saved places (Del removes one)",
        "",
        "Online and offline",
        "  O                    Switch online / offline. Offline never opens a connection.",
        "  D                    Save the tiles of this view (and two zoom levels in) for offline use",
        "  R  F5                Ask again for tiles that failed",
        "",
        "Legend",
        "  ~ water    \" green space    . : - = + * # % @  more drawn there (roads, buildings, labels)",
        "  X the centre    P a saved place    / a tile that is not available    . a tile still loading",
        "",
        "Offline data",
        "  Visited tiles are saved as tiles/z/x/y.png in the map folder, and any raster .mbtiles file put there",
        "  is read too. The folder is shown in the status line after pressing D.",
        "",
        "Press any key to go back.",
    ];

    private readonly MapModel _model;
    private readonly IMapSearchService _search;
    private Mode _mode = Mode.Browse;
    private string _appliedTitle = string.Empty;

    private string _input = string.Empty;
    private ListKind _listKind = ListKind.Places;
    private List<MapPlace> _list = [];

    /// <summary>True while "Remove this saved place? (y/n)" is on the status line: the next key answers it.</summary>
    private bool _confirmingRemovePlace;
    private int _listIndex;
    private int _listScroll;
    private int _helpScroll;
    private string _status = string.Empty;
    private MapPlace? _lastResult;
    private bool _searching;
    private volatile SearchOutcome? _pending;

    private MapFrame? _frame;
    private (double, double, int, int, int, int, int, int) _frameKey;
    private int _drawnVersion = -1;
    private int _mapX, _mapY, _mapColumns, _mapRows;

    private sealed record SearchOutcome(string Query, IReadOnlyList<MapPlace> Places, string? Error);

    public MapDialog(IMapTileService tiles, IMapSearchService search, MapStore store, IThemeService themeService)
        : this(new MapModel(tiles, store), search, themeService)
    {
    }

    internal MapDialog(MapModel model, IMapSearchService search, IThemeService themeService)
        : base(nameof(MapDialog), themeService)
    {
        _model = model;
        _search = search;
        Title = "Map";
    }

    internal MapModel Model => _model;

    internal string Status => _status;

    internal bool IsTyping => _mode == Mode.Search;

    internal bool ShowsList => _mode == Mode.List;

    internal bool ConfirmingRemovePlace => _confirmingRemovePlace;

    internal bool ShowsHelp => _mode == Mode.Help;

    internal string InputText => _input;

    internal MapFrame? Frame => _frame;

    internal IReadOnlyList<MapPlace> ListItems => _list;

    #region Lifecycle

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        _mode = Mode.Browse;
        _frame = null;
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            ViewSize = Parent.ViewSize;
            Position = new Position(0, 0);
        }

        base.OnInitializeLayout();
    }

    public override void OnIdle()
    {
        base.OnIdle();

        var changed = false;
        if (_pending is { } outcome)
        {
            _pending = null;
            _searching = false;
            ShowResults(outcome);
            changed = true;
        }

        if (_model.Version != _drawnVersion)
        {
            changed = true;
        }

        var title = _model.Online ? "Map" : "Map (offline)";
        if (title != _appliedTitle)
        {
            _appliedTitle = title;
            Title = title;
            changed = true;
        }

        if (changed)
        {
            StateHasChanged();
        }
    }

    #endregion

    #region Keys

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType != KeyPressType.Down)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        var handled = _mode switch
        {
            Mode.Search => HandleSearchKey(keyEvent),
            Mode.List => HandleListKey(keyEvent),
            Mode.Help => HandleHelpKey(keyEvent),
            _ => HandleBrowseKey(keyEvent),
        };

        if (handled)
        {
            StateHasChanged();
            return true;
        }

        // Anything the map does not claim must not reach Dialog's Tab and Esc commands.
        if (keyEvent.ConsoleKey is ConsoleKey.Tab or ConsoleKey.Escape && !keyEvent.Control && !keyEvent.Alt)
        {
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private bool HandleBrowseKey(KeyEvent key)
    {
        if (key.Control || key.Alt)
        {
            if (key.Control && !key.Alt && key.ConsoleKey == ConsoleKey.D)
            {
                SaveCenterAsPlace();
                return true;
            }

            if (key.Control && !key.Alt && key.ConsoleKey == ConsoleKey.F)
            {
                BeginSearch();
                return true;
            }

            return false;
        }

        var columns = Math.Max(1, _mapColumns);
        var rows = Math.Max(1, _mapRows);
        var stepX = key.Shift ? columns / 2.0 : Math.Max(1, columns / 8);
        var stepY = key.Shift ? rows / 2.0 : Math.Max(1, rows / 8);
        switch (key.ConsoleKey)
        {
            case ConsoleKey.Escape:
                if (_status.Length > 0)
                {
                    _status = string.Empty;
                }
                else
                {
                    Close();
                }

                return true;
            case ConsoleKey.LeftArrow:
                _model.Pan(-stepX, 0);
                return true;
            case ConsoleKey.RightArrow:
                _model.Pan(stepX, 0);
                return true;
            case ConsoleKey.UpArrow:
                _model.Pan(0, -stepY);
                return true;
            case ConsoleKey.DownArrow:
                _model.Pan(0, stepY);
                return true;
            case ConsoleKey.PageUp:
            case ConsoleKey.Add:
            case ConsoleKey.OemPlus:
                _model.ZoomBy(1);
                return true;
            case ConsoleKey.PageDown:
            case ConsoleKey.Subtract:
            case ConsoleKey.OemMinus:
                _model.ZoomBy(-1);
                return true;
            case ConsoleKey.Home:
                _model.CenterOn(MapStore.Home.Latitude, MapStore.Home.Longitude, MapStore.HomeZoom);
                return true;
            case ConsoleKey.F1:
                _mode = Mode.Help;
                _helpScroll = 0;
                return true;
            case ConsoleKey.F2:
            case ConsoleKey.B:
                OpenPlaces();
                return true;
            case ConsoleKey.F5:
            case ConsoleKey.R:
                _model.Retry();
                return true;
            case ConsoleKey.F6:
            case ConsoleKey.G:
            case ConsoleKey.Divide:
            case ConsoleKey.Oem2:
                BeginSearch();
                return true;
            case ConsoleKey.M:
                SaveCenterAsPlace();
                return true;
            case ConsoleKey.O:
                _model.SetOnline(!_model.Online);
                _status = _model.Message;
                return true;
            case ConsoleKey.D:
                _model.StartSavingForOffline(columns, rows);
                _status = _model.Message;
                return true;
        }

        if (key.KeyChar is '+' or '=')
        {
            _model.ZoomBy(1);
            return true;
        }

        if (key.KeyChar is '-' or '_')
        {
            _model.ZoomBy(-1);
            return true;
        }

        if (key.KeyChar == '/')
        {
            BeginSearch();
            return true;
        }

        return false;
    }

    private void BeginSearch()
    {
        _mode = Mode.Search;
        _input = string.Empty;
        _status = string.Empty;
    }

    private bool HandleSearchKey(KeyEvent key)
    {
        if (PasteHelper.IsPasteKey(key))
        {
            PasteHelper.Paste(text => _input += PasteHelper.SingleLine(text), StateHasChanged);
            return true;
        }

        switch (key.ConsoleKey)
        {
            case ConsoleKey.Escape:
                _mode = Mode.Browse;
                return true;
            case ConsoleKey.Enter:
                _mode = Mode.Browse;
                StartSearch(_input.Trim());
                return true;
            case ConsoleKey.Backspace:
                if (_input.Length > 0)
                {
                    _input = _input[..^1];
                }

                return true;
            case ConsoleKey.U when key.Control:
                _input = string.Empty;
                return true;
        }

        if (ConsoleKeyHelper.IsTypingKey(key))
        {
            _input += key.KeyChar;
            return true;
        }

        return false;
    }

    private void StartSearch(string query)
    {
        if (query.Length == 0)
        {
            return;
        }

        if (!MapProjection.TryParseCoordinates(query, out _, out _) && !_model.Online)
        {
            _status = "Searching for a name needs the network. Press O to go online, or type coordinates.";
            return;
        }

        if (_searching)
        {
            _status = "A search is already running.";
            return;
        }

        _searching = true;
        _status = $"Searching for “{query}”…";
        _ = Task.Run(async () =>
        {
            try
            {
                _pending = new SearchOutcome(query, await _search.SearchAsync(query), null);
            }
            catch (Exception exception) when (exception is WebFetchException or HttpRequestException or OperationCanceledException)
            {
                _pending = new SearchOutcome(query, [], exception.Message);
            }
        });
    }

    private void ShowResults(SearchOutcome outcome)
    {
        if (outcome.Error is not null)
        {
            _status = outcome.Error;
            return;
        }

        if (outcome.Places.Count == 0)
        {
            _status = $"Nothing found for “{outcome.Query}”.";
            return;
        }

        _status = string.Empty;
        if (outcome.Places.Count == 1)
        {
            GoTo(outcome.Places[0]);
            return;
        }

        _list = [.. outcome.Places];
        _listKind = ListKind.Results;
        _listIndex = 0;
        _listScroll = 0;
        _mode = Mode.List;
        _confirmingRemovePlace = false;
    }

    private void GoTo(MapPlace place)
    {
        _lastResult = place;
        _model.CenterOn(place.Latitude, place.Longitude, Math.Max(_model.Zoom, 14));
        _status = place.Name;
    }

    private void OpenPlaces()
    {
        _list = [.. _model.Store.Places];
        _listKind = ListKind.Places;
        _listIndex = 0;
        _listScroll = 0;
        if (_list.Count == 0)
        {
            _status = "No saved places yet. Press M to save the centre of the map.";
            return;
        }

        _mode = Mode.List;
        _confirmingRemovePlace = false;
    }

    private void RemoveSelectedPlace()
    {
        _model.Store.RemovePlace(_list[_listIndex]);
        _list.RemoveAt(_listIndex);
        _listIndex = Math.Min(_listIndex, _list.Count - 1);
        if (_list.Count == 0)
        {
            _mode = Mode.Browse;
        }
    }

    private void SaveCenterAsPlace()
    {
        var name = _lastResult is { } last
                   && Math.Abs(last.Latitude - _model.Latitude) < 1e-6 && Math.Abs(last.Longitude - _model.Longitude) < 1e-6
            ? last.Name
            : MapProjection.FormatCoordinates(_model.Latitude, _model.Longitude);
        _model.Store.AddPlace(new MapPlace(name, _model.Latitude, _model.Longitude));
        _status = "Saved: " + name;
    }

    private bool HandleListKey(KeyEvent key)
    {
        if (_confirmingRemovePlace)
        {
            // Esc is the list's own and leaves it as it closes the question; any other key but y withdraws it.
            _confirmingRemovePlace = false;
            _status = string.Empty;
            if (key.ConsoleKey == ConsoleKey.Y && !key.Control && !key.Alt)
            {
                RemoveSelectedPlace();
                return true;
            }

            if (key.ConsoleKey != ConsoleKey.Escape)
            {
                return true;
            }
        }

        var page = Math.Max(1, _mapRows - 2);
        switch (key.ConsoleKey)
        {
            case ConsoleKey.Escape:
                _mode = Mode.Browse;
                return true;
            case ConsoleKey.UpArrow:
                _listIndex = Math.Max(0, _listIndex - 1);
                break;
            case ConsoleKey.DownArrow:
                _listIndex = Math.Min(_list.Count - 1, _listIndex + 1);
                break;
            case ConsoleKey.PageUp:
                _listIndex = Math.Max(0, _listIndex - page);
                break;
            case ConsoleKey.PageDown:
                _listIndex = Math.Min(_list.Count - 1, _listIndex + page);
                break;
            case ConsoleKey.Home:
                _listIndex = 0;
                break;
            case ConsoleKey.End:
                _listIndex = _list.Count - 1;
                break;
            case ConsoleKey.Enter when _list.Count > 0:
                _mode = Mode.Browse;
                GoTo(_list[_listIndex]);
                return true;
            case ConsoleKey.Delete when _listKind == ListKind.Places && _list.Count > 0:
                _confirmingRemovePlace = true;
                _status = $"Remove the saved place \"{_list[_listIndex].Name}\"? (y/n)";
                return true;
            default:
                return true;
        }

        var visible = Math.Max(1, _mapRows - 1);
        if (_listIndex < _listScroll)
        {
            _listScroll = _listIndex;
        }
        else if (_listIndex >= _listScroll + visible)
        {
            _listScroll = _listIndex - visible + 1;
        }

        return true;
    }

    private bool HandleHelpKey(KeyEvent key)
    {
        switch (key.ConsoleKey)
        {
            case ConsoleKey.UpArrow:
                _helpScroll = Math.Max(0, _helpScroll - 1);
                return true;
            case ConsoleKey.DownArrow:
                _helpScroll = Math.Min(Math.Max(0, HelpLines.Length - _mapRows), _helpScroll + 1);
                return true;
        }

        _mode = Mode.Browse;
        return true;
    }

    #endregion

    #region Mouse

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        var column = (int)mouseEvent.Position.X - _mapX;
        var row = (int)mouseEvent.Position.Y - _mapY;
        var inMap = _mode == Mode.Browse && column >= 0 && column < _mapColumns && row >= 0 && row < _mapRows;

        if (mouseEvent.WheelDelta != 0)
        {
            if (_mode == Mode.Browse)
            {
                var delta = mouseEvent.WheelDelta > 0 ? 1 : -1;
                if (inMap)
                {
                    _model.ZoomAround(delta, column, row, _mapColumns, _mapRows);
                }
                else
                {
                    _model.ZoomBy(delta);
                }
            }
            else if (_mode == Mode.List)
            {
                _listIndex = Math.Clamp(_listIndex - Math.Sign(mouseEvent.WheelDelta), 0, Math.Max(0, _list.Count - 1));
                _listScroll = Math.Clamp(_listScroll, Math.Max(0, _listIndex - _mapRows + 2), _listIndex);
            }

            StateHasChanged();
            return true;
        }

        if (!mouseEvent.LeftClicked || mouseEvent.IsRelease)
        {
            return await base.HandleMouseEventAsync(mouseEvent);
        }

        if (inMap)
        {
            var (latitude, longitude) = _model.CellToCoordinate(column, row, _mapColumns, _mapRows);
            _model.CenterOn(latitude, longitude);
            StateHasChanged();
            return true;
        }

        if (_mode == Mode.List && column >= 0 && column < _mapColumns && row >= 1 && row - 1 + _listScroll < _list.Count)
        {
            _listIndex = row - 1 + _listScroll;
            _mode = Mode.Browse;
            GoTo(_list[_listIndex]);
            StateHasChanged();
            return true;
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    #endregion

    #region Draw

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        var width = (int)ViewSize.Width - 2;
        var height = (int)ViewSize.Height - 2;
        if (width < 20 || height < ChromeRows + 3)
        {
            return;
        }

        var scheme = ThemeService.CurrentColorScheme;
        var x = (int)AbsolutePosition.X + 1;
        var y = (int)AbsolutePosition.Y + 1;
        _mapX = x;
        _mapY = y + 1;
        _mapColumns = width;
        _mapRows = height - ChromeRows;

        DrawHeader(buffer, scheme, x, y, width);
        switch (_mode)
        {
            case Mode.List:
                DrawList(buffer, scheme, x, y + 1, width, _mapRows);
                break;
            case Mode.Help:
                DrawHelp(buffer, scheme, x, y + 1, width, _mapRows);
                break;
            default:
                DrawMap(buffer, scheme, x, y + 1, width, _mapRows);
                break;
        }

        DrawStatus(buffer, scheme, x, y + height - 1, width);
    }

    private void DrawHeader(IBuffer buffer, ColorScheme scheme, int x, int y, int width)
    {
        var left = $" {(_model.Online ? "ONLINE" : "OFFLINE")}  {MapProjection.FormatCoordinates(_model.Latitude, _model.Longitude)}  zoom {_model.Zoom}/{MapProjection.MaxZoom}";
        var right = width >= left.Length + Attribution.Length + 3 ? Attribution + " " : string.Empty;
        var line = left.PadRight(Math.Max(0, width - right.Length)) + right;
        WriteText(buffer, x, y, width, line, _model.Online ? Color.Cyan : Color.Yellow, scheme.Background, true);
    }

    private void DrawMap(IBuffer buffer, ColorScheme scheme, int x, int y, int width, int height)
    {
        var key = (_model.Latitude, _model.Longitude, _model.Zoom, width, height, _model.Version, _model.Online ? 1 : 0, _model.Store.Places.Count);
        if (_frame is null || key != _frameKey)
        {
            _frameKey = key;
            _drawnVersion = _model.Version;
            _frame = _model.BuildFrame(width, height, _model.Store.Places);
        }

        var frame = _frame;
        var line = new Conxel[width];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var cell = frame[column, row];
                line[column] = new Conxel(cell.Char, cell.Foreground, scheme.Background, cell.Bold);
            }

            buffer.Write(ZOrder, new Position(x, y + row), new Size(width, 1), new ConxelString(line));
        }
    }

    private void DrawList(IBuffer buffer, ColorScheme scheme, int x, int y, int width, int height)
    {
        var heading = _listKind == ListKind.Places ? "Saved places" : "Search results";
        var hint = _listKind == ListKind.Places ? "Enter:go   Del:remove   Esc:back" : "Enter:go   Esc:back";
        WriteText(buffer, x, y, width, $" {heading} ({_list.Count})   {hint}", Color.Yellow, scheme.Background, true);
        for (var row = 0; row < height - 1; row++)
        {
            var index = _listScroll + row;
            if (index >= _list.Count)
            {
                WriteText(buffer, x, y + 1 + row, width, string.Empty, scheme.Foreground, scheme.Background, false);
                continue;
            }

            var place = _list[index];
            var text = $" {Shorten(place.Name, Math.Max(1, width - 32))}".PadRight(Math.Max(1, width - 30)) + MapProjection.FormatCoordinates(place.Latitude, place.Longitude);
            var selected = index == _listIndex;
            WriteText(buffer, x, y + 1 + row, width, text,
                selected ? scheme.Background : scheme.Foreground,
                selected ? scheme.Foreground : scheme.Background, false);
        }
    }

    private void DrawHelp(IBuffer buffer, ColorScheme scheme, int x, int y, int width, int height)
    {
        for (var row = 0; row < height; row++)
        {
            var index = _helpScroll + row;
            var text = index < HelpLines.Length ? HelpLines[index] : string.Empty;
            var heading = index < HelpLines.Length && (index == 0 || text is "Moving" or "Zoom" or "Finding places" or "Online and offline" or "Legend" or "Offline data");
            WriteText(buffer, x, y + row, width, " " + text, heading ? Color.Yellow : scheme.Foreground, scheme.Background, heading);
        }
    }

    private void DrawStatus(IBuffer buffer, ColorScheme scheme, int x, int y, int width)
    {
        string left;
        var color = scheme.Foreground;
        if (_mode == Mode.Search)
        {
            left = " Search or coordinates: " + _input + "█   Enter:go   Esc:cancel";
        }
        else if (_mode == Mode.Help)
        {
            left = " Up/Down:scroll   any other key:back";
            color = scheme.DisabledForeground;
        }
        else if (_status.Length > 0)
        {
            left = " " + _status;
            color = Color.Cyan;
        }
        else if (_model.Message is { Length: > 0 } message)
        {
            left = " " + message;
            color = Color.Cyan;
        }
        else
        {
            left = " F1:help  Arrows:pan  +/-:zoom  /:search  M:save place  B:places  O:online/offline  D:save offline  Esc:close";
            color = scheme.DisabledForeground;
        }

        var right = _mode == Mode.Browse ? " " + Scale() + " " + LoadingNote() : string.Empty;
        left = Shorten(left, Math.Max(1, width - right.Length)).PadRight(Math.Max(0, width - right.Length)) + right;
        WriteText(buffer, x, y, width, left, color, scheme.Background, false);
    }

    private string LoadingNote()
    {
        if (_frame is not { } frame)
        {
            return string.Empty;
        }

        return frame.Loading > 0 ? $"[{frame.Loading} loading]"
            : frame.Unavailable > 0 ? $"[{frame.Unavailable} missing]"
            : string.Empty;
    }

    /// <summary>A scale bar for the current zoom: "|----| 2 km", as long as a round distance fits in 20 characters.</summary>
    private string Scale()
    {
        var metersPerCell = MapProjection.MetersPerPixel(_model.Latitude, _model.Zoom) * MapAsciiRenderer.CellWidth;
        if (metersPerCell <= 0)
        {
            return string.Empty;
        }

        var distance = MapProjection.NiceDistance(metersPerCell * 20);
        var cells = Math.Max(2, (int)Math.Round(distance / metersPerCell));
        return "|" + new string('-', cells - 2) + "| " + MapProjection.FormatDistance(distance);
    }

    private void WriteText(IBuffer buffer, int x, int y, int width, string text, Color foreground, Color background, bool bold)
    {
        text = text.Length > width ? text[..width] : text.PadRight(width);
        buffer.Write(ZOrder, new Position(x, y), new Size(width, 1), new ConxelString(text, foreground, background, bold));
    }

    private static string Shorten(string text, int max)
    {
        if (max <= 0)
        {
            return string.Empty;
        }

        return text.Length <= max ? text : max <= 1 ? text[..max] : text[..(max - 1)] + "…";
    }

    #endregion
}

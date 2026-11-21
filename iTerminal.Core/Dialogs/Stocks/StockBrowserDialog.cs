using iConsole.Localization;
using System.Globalization;
using iConsole.Buffers;
using iConsole.Controls.Charts;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Services.Stocks;

namespace iTerminal.Core.Dialogs.Stocks;

/// <summary>
/// Full-screen stock browser (Alt+F7): a live list of the stocks on the watchlist — price, change
/// since the previous close, exchange — that keeps itself fresh while the dialog is open, and a way to
/// add and remove stocks. Adding searches by symbol or company name across every exchange the quote
/// source lists and shows the exchange of each hit, because one company is listed on several.
///
/// <para>The dialog never waits on the network: quotes are fetched by <see cref="IStockService"/> on its
/// own refresh loop (started while this dialog is open, stopped when it closes), a change comes back as
/// an event, and searches and adds run on background tasks.</para>
/// </summary>
public class StockBrowserDialog : SplitPaneDialog
{
    private const int SymbolWidth = 10;
    private const int PriceWidth = 11;
    private const int ChangeWidth = 10;
    private const int PercentWidth = 9;
    private const int ExchangeWidth = 14;
    private const int MinNameWidth = 14;

    internal sealed record StockRow(WatchedStock Stock, StockQuote? Quote);

    /// <summary>One layout for the rows and the headings above them.</summary>
    internal static string FormatColumns(string symbol, string name, string price, string change, string percent, string exchange, int width)
    {
        // Columns go from the right when the company name would get fewer than MinNameWidth: the exchange first, then the
        // change in percent, then the change. The price stays. At 45 columns all of them stayed and the name got 3 ("Nam").
        int[] extra = [ChangeWidth + 1, PercentWidth + 1, ExchangeWidth + 2];
        var keep = extra.Length;
        int Room(int kept) => width - 1 - SymbolWidth - (PriceWidth + 1) - extra.Take(kept).Sum();
        while (keep > 0 && Room(keep) < MinNameWidth)
        {
            keep--;
        }

        var nameWidth = Math.Max(4, Room(keep));
        var text = " " + Cut(symbol, SymbolWidth - 1).PadRight(SymbolWidth) + Cut(name, nameWidth - 1).PadRight(nameWidth) + $" {price,PriceWidth}"
            + (keep >= 1 ? $" {change,ChangeWidth}" : string.Empty)
            + (keep >= 2 ? $" {percent,PercentWidth}" : string.Empty)
            + (keep >= 3 ? "  " + Cut(exchange, ExchangeWidth - 2) : string.Empty);
        return Cut(text, width);
    }

    /// <summary>
    /// The parts joined by three spaces, as many as fit <paramref name="width"/>, from the first: a part is shown whole or not at all, so a line
    /// never ends in half a figure ("updated 19:0", "last tr"). The first part alone is cut, with an ellipsis, only when it is wider than the line.
    /// </summary>
    internal static string FitParts(IReadOnlyList<string> parts, int width)
    {
        if (parts.Count == 0 || width <= 0)
        {
            return string.Empty;
        }

        var text = parts[0];
        if (text.Length > width)
        {
            return width <= 1 ? text[..width] : text[..(width - 1)] + "…";
        }

        for (var i = 1; i < parts.Count; i++)
        {
            var next = text + "   " + parts[i];
            if (next.Length > width)
            {
                break;
            }

            text = next;
        }

        return text;
    }

    private static string Cut(string text, int width) => text.Length <= width ? text : text[..Math.Max(0, width)];

    internal static string FormatPrice(decimal price) =>
        price.ToString(Math.Abs(price) >= 1000m ? "N2" : "0.00##", CultureInfo.InvariantCulture);

    internal static string FormatChange(decimal value) =>
        (value > 0 ? "+" : string.Empty) + FormatPrice(value);

    internal static string FormatPercent(decimal percent) =>
        (percent > 0 ? "+" : string.Empty) + percent.ToString("0.00", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// The watchlist's columns for a line this wide. The company name takes what the others leave; the columns on the right
    /// go first when it would get fewer than <see cref="MinNameWidth"/> (the exchange, then the change in percent, then the change).
    /// The price stays.
    /// </summary>
    internal static List<ColumnDefinition<StockRow>> WatchlistColumns(int width)
    {
        int[] extra = [ChangeWidth, PercentWidth, ExchangeWidth];
        var keep = extra.Length;
        // Three columns plus the kept ones, with one space between each pair.
        int Room(int kept) => width - SymbolWidth - PriceWidth - extra.Take(kept).Sum() - (2 + kept);
        while (keep > 0 && Room(keep) < MinNameWidth)
        {
            keep--;
        }

        var columns = new List<ColumnDefinition<StockRow>>
        {
            new(UiText.Get("Stocks.ColSymbol", "Symbol"), SymbolWidth, row => row.Stock.Symbol),
            new(UiText.Get("Stocks.ColName", "Name"), Math.Max(4, Room(keep)), row => row.Quote?.Name ?? row.Stock.Name),
            new(UiText.Get("Stocks.ColPrice", "Price"), PriceWidth, row => row.Quote is null ? "…" : FormatPrice(row.Quote.Price), TextAlignment.Right),
        };
        if (keep >= 1)
        {
            columns.Add(new(UiText.Get("Stocks.ColChange", "Change"), ChangeWidth, row => row.Quote is null ? string.Empty : FormatChange(row.Quote.Change), TextAlignment.Right));
        }

        if (keep >= 2)
        {
            columns.Add(new(UiText.Get("Stocks.ColChangePct", "Change %"), PercentWidth, row => row.Quote is null ? string.Empty : FormatPercent(row.Quote.ChangePercent), TextAlignment.Right));
        }

        if (keep >= 3)
        {
            columns.Add(new(UiText.Get("Stocks.ColExchange", "Exchange"), ExchangeWidth, row => row.Quote?.Exchange ?? string.Empty));
        }

        return columns;
    }

    /// <summary>The add-stock hits' columns for a line this wide: the company name takes what is left after the symbol, the exchange and the type.</summary>
    internal static List<ColumnDefinition<StockSearchHit>> HitColumns(int width)
    {
        var nameWidth = Math.Max(4, width - 1 - SymbolWidth - ExchangeWidth - 6);
        return
        [
            new(UiText.Get("Stocks.ColSymbol", "Symbol"), SymbolWidth, hit => hit.Symbol),
            new(UiText.Get("Stocks.ColCompany", "Company"), nameWidth, hit => hit.Name),
            new(UiText.Get("Stocks.ColExchange", "Exchange"), ExchangeWidth, hit => hit.Exchange),
            new(UiText.Get("Stocks.ColType", "Type"), Math.Max(1, width - SymbolWidth - nameWidth - ExchangeWidth - 3), hit => hit.Type),
        ];
    }

    private readonly IStockService _stockService;
    private readonly ConcurrentObservableCollection<StockRow> _rowItems = new();
    private readonly ConcurrentObservableCollection<StockSearchHit> _hitItems = new();
    private readonly Table<StockRow> _stockTable;
    private readonly Table<StockSearchHit> _hitTable;
    private readonly TextBox _inputBox;

    private IDisposable? _watch;
    private string _message = string.Empty;
    private volatile bool _isBusy;

    /// <summary>Whether the add-stock flow (the query box, then the hits) is up. Tracked here: <c>ShowAsync</c> makes every child visible, so a control's own flag cannot say.</summary>
    private bool _adding;
    private bool _inputActive;
    private string _searchedQuery = string.Empty;
    private string _confirmPrompt = string.Empty;
    private Action? _confirmAction;

    public StockBrowserDialog(
        IStockService stockService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(StockBrowserDialog), themeService)
    {
        _stockService = stockService;
        Title = iConsole.Localization.UiText.Get("Dialog.Stocks", "Stocks");

        _stockTable = new Table<StockRow>(_rowItems, themeService)
        {
            EmptyText = UiText.Get("Empty.Watchlist", " The watchlist is empty — press a to add a stock."),
            // A quote that rose is green and one that fell is red, on rows that are not under the cursor.
            RowForeground = row => row.Quote?.Change switch
            {
                > 0 => Color.Green,
                < 0 => Color.Red,
                _ => null,
            },
        };
        _hitTable = new Table<StockSearchHit>(_hitItems, themeService)
        {
            EmptyText = UiText.Get("Empty.StockNothing", " Nothing found."),
        };
        _inputBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            IsVisible = false,
            IsEnabled = false,
            CanFocus = false,
        };

        AddControl(_stockTable);
        AddControl(_hitTable);
        AddControl(_inputBox);
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        _stockService.Changed += OnStocksChanged;
        _watch ??= _stockService.Watch();
        RecomputeRows();
    }

    public override void Close()
    {
        _stockService.Changed -= OnStocksChanged;
        _watch?.Dispose();
        _watch = null;
        EndAdding();
        base.Close();
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
            // The table's first row is its heading, so it starts one row above the first entry, as the section heading did.
            var listHeight = Math.Max(0, ih - 5);
            _stockTable.Position = new Position(1, 2);
            _stockTable.ViewSize = new Size(iw, listHeight + 1);
            _stockTable.ScrollableModel.ViewSize = new Size(iw, listHeight);
            _stockTable.Columns.Clear();
            _stockTable.Columns.AddRange(WatchlistColumns(iw));
            _hitTable.Position = new Position(1, 2);
            _hitTable.ViewSize = new Size(iw, listHeight + 1);
            _hitTable.ScrollableModel.ViewSize = new Size(iw, listHeight);
            _hitTable.Columns.Clear();
            _hitTable.Columns.AddRange(HitColumns(iw));

            _inputBox.Position = new Position(1, ih);
            _inputBox.ViewSize = new Size(iw, 1);
            _inputBox.ScrollableModel.ViewSize = _inputBox.ViewSize;
        }

        // Children must have their Position/ViewSize assigned before base.OnInitializeLayout() recurses into them.
        base.OnInitializeLayout();
    }

    #region Rows

    private void OnStocksChanged(object? sender, EventArgs e) => RecomputeRows();

    /// <summary>Re-reads the watchlist and its quotes — in memory, no I/O — and keeps the cursor where it was.</summary>
    private void RecomputeRows()
    {
        var rows = _stockService.Watchlist
            .Select(stock => new StockRow(stock, _stockService.GetQuote(stock.Symbol)))
            .ToList();
        _rowItems.ReplaceAll(rows);
        ClampCursor(_stockTable.ScrollableModel, rows.Count);
        StateHasChanged();
    }

    private static void ClampCursor(IScrollableModel scrollableModel, int count)
    {
        scrollableModel.CursorPosition.Y = count <= 0 ? 0 : Math.Clamp(scrollableModel.CursorPosition.Y, 0, count - 1);
        scrollableModel.ScrollPosition.Y = count <= 0 ? 0 : Math.Clamp(scrollableModel.ScrollPosition.Y, 0, count - 1);
    }

    private static T? CursorItem<T>(Table<T> table) where T : class => table.SelectedRow;

    private void SetMessage(string message)
    {
        _message = message;
        StateHasChanged();
    }

    private void RefreshNow()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        SetMessage(UiText.Get("Msg.UpdatingQuotes", "Updating quotes…"));
        _ = Task.Run(async () =>
        {
            try
            {
                await _stockService.RefreshAsync();
                SetMessage(_stockService.LastError);
            }
            finally
            {
                _isBusy = false;
            }
        });
    }

    #endregion

    #region Adding

    private void BeginAdding()
    {
        _adding = true;
        _searchedQuery = string.Empty;
        _hitItems.ReplaceAll([]);
        ShowInput(iConsole.Localization.UiText.Get("Stocks.FindPrompt", "Find stock by symbol or company name"));
        ApplyVisibility();
        SetMessage(string.Empty);
    }

    private void EndAdding()
    {
        if (!_adding)
        {
            return;
        }

        _adding = false;
        _inputActive = false;
        _inputBox.ResetFocus();
        _inputBox.Text = string.Empty;
        ApplyVisibility();
        _stockTable.SetFocus();
        StateHasChanged();
    }

    private void ShowInput(string label)
    {
        _inputActive = true;
        _inputBox.Label = label;
        _inputBox.Text = string.Empty;
        ApplyVisibility();
        _inputBox.SetFocus();
    }

    private void ApplyVisibility()
    {
        _inputBox.CanFocus = _inputActive;
        _inputBox.IsVisible = _inputActive;
        _inputBox.IsEnabled = _inputActive;
        _hitTable.IsVisible = _adding;
        _stockTable.IsVisible = !_adding;
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        ApplyVisibility();
    }

    /// <summary>Enter in the query box: search for it, or — when the search found nothing — add the text as a symbol.</summary>
    private void SubmitQuery()
    {
        var query = _inputBox.Text.Trim();
        if (query.Length == 0 || _isBusy)
        {
            return;
        }

        if (query == _searchedQuery && _hitItems.Count == 0)
        {
            AddSymbol(query, name: null);
            return;
        }

        _isBusy = true;
        SetMessage(UiText.Get("Msg.Searching", "Searching…"));
        _ = Task.Run(async () =>
        {
            try
            {
                var hits = await _stockService.SearchAsync(query);
                _searchedQuery = query;
                _hitItems.ReplaceAll([.. hits]);
                ClampCursor(_hitTable.ScrollableModel, hits.Count);
                SetMessage(hits.Count == 0
                    ? string.Format(UiText.Get("Stocks.NoneFound", "No stock found for \"{0}\". Enter again adds it as a symbol (exchange suffix: VOD.L)."), query)
                    : string.Format(UiText.Get("Stocks.Found", "{0} found — ↑↓ choose, Enter adds the chosen one"), hits.Count));
            }
            catch (Exception exception)
            {
                // Any failure is said here: only the network ones were, so a bad reply left "Searching…" on the screen.
                SetMessage(string.Format(UiText.Get("Msg.SearchFailed", "Error: search failed — {0}"), exception.Message));
            }
            finally
            {
                _isBusy = false;
            }
        });
    }

    private void AddChosenHit()
    {
        if (CursorItem(_hitTable) is { } hit)
        {
            AddSymbol(hit.Symbol, hit.Name);
        }
    }

    private void AddSymbol(string symbol, string? name)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        SetMessage(string.Format(UiText.Get("Msg.AddingSymbol", "Adding {0}…"), symbol.ToUpperInvariant()));
        _ = Task.Run(async () =>
        {
            try
            {
                var stock = await _stockService.AddAsync(symbol, name);
                _closeAdding = true;
                SetMessage(string.Format(UiText.Get("Msg.AddedSymbol", "Added {0}"), stock.Symbol));
            }
            catch (Exception exception)
            {
                // Any failure is said here, as for the search: a non-network one left "Adding…" on the screen.
                SetMessage(string.Format(UiText.Get("Msg.ErrorPrefix", "Error: {0}"), exception.Message));
            }
            finally
            {
                _isBusy = false;
            }
        });
    }

    private volatile bool _closeAdding;

    /// <summary>Leaves the add flow once an add has finished: done here, not where the add ended — that is a worker thread, and the tree is not its to change.</summary>
    public override void OnIdle()
    {
        base.OnIdle();
        if (_closeAdding)
        {
            _closeAdding = false;
            EndAdding();
        }
    }

    #endregion

    #region Removing

    private void BeginRemove()
    {
        if (CursorItem(_stockTable) is not { } row)
        {
            return;
        }

        var symbol = row.Stock.Symbol;
        _confirmPrompt = string.Format(UiText.Get("Stocks.ConfirmRemove", "Remove {0} from the list?  y:yes  any other key:no"), symbol);
        _confirmAction = () =>
        {
            _ = Task.Run(() =>
            {
                SetMessage(_stockService.Remove(symbol) ? string.Format(UiText.Get("Stocks.Removed", "Removed {0}"), symbol) : string.Format(UiText.Get("Stocks.NotOnList", "{0} is not on the list"), symbol));
            });
        };
        StateHasChanged();
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

    #region Input

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_confirmAction is not null)
        {
            yield return ("Y", iConsole.Localization.UiText.Get("Common.Confirm", "Confirm"));
            yield return ("Any other key", iConsole.Localization.UiText.Get("Common.Cancel", "Cancel"));
            yield break;
        }

        if (_adding)
        {
            yield return ("Enter", UiText.Get("DialogHint.SearchOrAddHit", "Search, or add the chosen hit"));
            yield return ("Up/Down", UiText.Get("DialogHint.ChooseHit", "Choose a hit"));
            yield return ("Esc", UiText.Get("DialogHint.BackToList", "Back to the list"));
            yield break;
        }

        yield return ("A", UiText.Get("DialogHint.AddStock", "Add a stock"));
        yield return ("D/Delete", UiText.Get("DialogHint.RemoveStock", "Remove the stock"));
        yield return ("R", UiText.Get("DialogHint.RefreshQuotes", "Refresh the quotes"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_closeAdding)
        {
            _closeAdding = false;
            EndAdding();
        }

        if (_confirmAction is not null)
        {
            return HandleConfirmKey(keyEvent);
        }

        if (_adding)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.Escape:
                    EndAdding();
                    return true;
                case ConsoleKey.Enter:
                    // The hits belong to the text they were searched for; after more typing Enter searches again.
                    if (_hitItems.Count > 0 && _inputBox.Text.Trim() == _searchedQuery)
                    {
                        AddChosenHit();
                    }
                    else
                    {
                        SubmitQuery();
                    }

                    return true;
                case ConsoleKey.UpArrow:
                case ConsoleKey.DownArrow:
                case ConsoleKey.PageUp:
                case ConsoleKey.PageDown:
                case ConsoleKey.Home:
                case ConsoleKey.End:
                    // The query box keeps the keyboard so typing always searches; the hits are driven from here.
                    return await _hitTable.HandleKeyEventAsync(keyEvent);
                case ConsoleKey.Tab:
                    // Nowhere else to go: Tab would leave the add flow.
                    return true;
            }

            return await base.HandleKeyEventAsync(keyEvent);
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Delete)
        {
            BeginRemove();
            return true;
        }

        switch (char.ToLowerInvariant(keyEvent.KeyChar))
        {
            case 'a':
                BeginAdding();
                return true;
            case 'd':
                BeginRemove();
                return true;
            case 'r':
                RefreshNow();
                return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    #endregion

    #region Draw

    internal static string KeyHints => iConsole.Localization.UiText.Get("Hints.StockBrowser", "a:add  d/Del:remove  r:refresh  Esc:close");
    internal static string AddKeyHints => iConsole.Localization.UiText.Get("Hints.StockBrowserAdd", "Enter:search/add  ↑↓:choose  Esc:back");

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

        DrawHeader(buffer, ix, iy, iw, colorScheme);

        if (ih >= 3)
        {
            DrawDetails(buffer, ix, iy + ih - 3, iw, colorScheme);
        }

        if (ih >= 2)
        {
            DrawSummary(buffer, ix, iy + ih - 2, iw, colorScheme);
        }

        if (!_inputActive)
        {
            DrawFooter(buffer, ix, iy + ih - 1, iw, colorScheme);
        }
    }

    private void DrawHeader(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        var updated = _stockService.LastUpdated;
        var text = _adding
            ? UiText.Get("Stocks.HeaderAdd", "Stocks — add a stock")
            : FitParts(
                [
                    string.Format(UiText.Get("Stocks.HeaderCount", "Stocks — {0} on the list"), _rowItems.Count),
                    updated is { } time ? string.Format(UiText.Get("Stocks.Live", "live, updated {0}"), time.ToString("HH:mm:ss")) : UiText.Get("Stocks.Waiting", "waiting for the first quotes…"),
                ],
                w);
        WriteLine(buffer, x, y, w, text, Color.Cyan, colorScheme.Background);
    }

    /// <summary>The cursor stock in more detail: the day's and the year's range, volume and last trade.</summary>
    private void DrawDetails(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        var text = string.Empty;
        if (!_adding && CursorItem(_stockTable) is { Quote: { } quote })
        {
            var parts = new List<string> { quote.Name };
            if (quote is { DayLow: { } dayLow, DayHigh: { } dayHigh })
            {
                parts.Add(string.Format(UiText.Get("Stocks.Day", "day {0}–{1}"), FormatPrice(dayLow), FormatPrice(dayHigh)));
            }

            if (quote is { YearLow: { } yearLow, YearHigh: { } yearHigh })
            {
                var position = yearHigh > yearLow ? (double)((quote.Price - yearLow) / (yearHigh - yearLow)) : 0;
                parts.Add(string.Format(UiText.Get("Stocks.Year", "52w {0} ▕{1}▏ {2}"), FormatPrice(yearLow), BarChart.BuildBar(position, 8), FormatPrice(yearHigh)));
            }

            parts.Add(string.Format(UiText.Get("Stocks.PrevClose", "prev close {0} {1}"), FormatPrice(quote.PreviousClose), quote.Currency));
            if (quote.Volume is { } volume)
            {
                parts.Add(string.Format(UiText.Get("Stocks.Volume", "vol {0}"), volume.ToString("N0", CultureInfo.InvariantCulture)));
            }

            if (quote.MarketTime is { } last)
            {
                parts.Add(string.Format(UiText.Get("Stocks.LastTrade", "last trade {0}"), last.ToString("yyyy-MM-dd HH:mm")));
            }

            text = " " + FitParts(parts, w - 1);
        }

        WriteLine(buffer, x, y, w, text, colorScheme.DisabledForeground, colorScheme.Background);
    }

    /// <summary>How many of the list are up and how many down since the previous close.</summary>
    private void DrawSummary(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        var quotes = _rowItems.Select(row => row.Quote).Where(q => q is not null).ToList();
        var up = quotes.Count(q => q!.Change > 0);
        var down = quotes.Count(q => q!.Change < 0);
        // While adding, the input box has taken the footer, so the status lives on this line.
        var summary = new List<string> { string.Format(UiText.Get("Stocks.Summary", "{0} up   {1} down   {2} unchanged"), up, down, quotes.Count - up - down) };
        if (_rowItems.Count > quotes.Count)
        {
            summary.Add(string.Format(UiText.Get("Stocks.NotQuoted", "{0} not quoted"), _rowItems.Count - quotes.Count));
        }

        var text = _adding ? " " + _message : " " + FitParts(summary, w - 1);
        WriteLine(buffer, x, y, w, text, colorScheme.Foreground, colorScheme.Background);
    }

    private void DrawFooter(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        if (_confirmAction is not null)
        {
            WriteLine(buffer, x, y, w, _confirmPrompt, colorScheme.Error, colorScheme.Background);
            return;
        }

        var status = _message.Length > 0 ? _message : _stockService.LastError;
        var foreground = status.StartsWith("Error", StringComparison.Ordinal) || _message.Length == 0 && status.Length > 0
            ? colorScheme.Error
            : colorScheme.Foreground;
        DrawStatusAndHints(buffer, x, y, w, status, foreground, _adding ? AddKeyHints : KeyHints, colorScheme);
    }

    internal string MessageForTest => _message;
    internal bool IsAddingForTest => _adding;
    internal bool IsConfirmingForTest => _confirmAction is not null;
    internal IReadOnlyList<StockRow> RowsForTest => [.. _rowItems.AsSnapshot()];
    /// <summary>
    /// How many stock and hit rows the lists have actually taken up. The collections above are filled first and
    /// the list boxes rebuilt right after on the same worker, so a test that renders as soon as the collection
    /// is full can draw the list before it has caught up; it waits on these instead.
    /// </summary>
    internal int StockRowsShownForTest => _rowItems.Count;
    internal int HitRowsShownForTest => _hitItems.Count;
    internal IReadOnlyList<StockSearchHit> HitsForTest => [.. _hitItems.AsSnapshot()];
    internal TextBox InputBoxForTest => _inputBox;

    #endregion
}

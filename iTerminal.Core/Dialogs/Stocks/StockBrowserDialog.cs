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

    internal sealed record StockRow(WatchedStock Stock, StockQuote? Quote);

    /// <summary>One layout for the rows and the headings above them.</summary>
    internal static string FormatColumns(string symbol, string name, string price, string change, string percent, string exchange, int width)
    {
        var nameWidth = Math.Max(4, width - 1 - SymbolWidth - PriceWidth - ChangeWidth - PercentWidth - ExchangeWidth - 5);
        var text = " " + Cut(symbol, SymbolWidth - 1).PadRight(SymbolWidth) + Cut(name, nameWidth - 1).PadRight(nameWidth)
            + $" {price,PriceWidth} {change,ChangeWidth} {percent,PercentWidth}  " + Cut(exchange, ExchangeWidth - 2);
        return Cut(text, width);
    }

    private static string Cut(string text, int width) => text.Length <= width ? text : text[..Math.Max(0, width)];

    internal static string FormatPrice(decimal price) =>
        price.ToString(Math.Abs(price) >= 1000m ? "N2" : "0.00##", CultureInfo.InvariantCulture);

    internal static string FormatChange(decimal value) =>
        (value > 0 ? "+" : string.Empty) + FormatPrice(value);

    internal static string FormatPercent(decimal percent) =>
        (percent > 0 ? "+" : string.Empty) + percent.ToString("0.00", CultureInfo.InvariantCulture) + "%";

    private sealed class StockListBox : ListBox<StockRow>
    {
        public StockListBox(IListBoxModel<StockRow> listModel, IThemeService themeService)
            : base(listModel, themeService)
        {
            ListModel.TransformItem = TransformRow;
        }

        private ListItem<StockRow> TransformRow(StockRow row, CancellationToken cancellationToken)
        {
            var quote = row.Quote;
            var text = quote is null
                ? FormatColumns(row.Stock.Symbol, row.Stock.Name, "…", string.Empty, string.Empty, string.Empty, (int)ViewSize.Width)
                : FormatColumns(
                    row.Stock.Symbol,
                    quote.Name,
                    FormatPrice(quote.Price),
                    FormatChange(quote.Change),
                    FormatPercent(quote.ChangePercent),
                    quote.Exchange,
                    (int)ViewSize.Width);
            return new ListItem<StockRow>(row) { Value = text, IsEnabled = true };
        }

        protected override Color? GetItemForegroundOverride(int index)
        {
            if (index < 0 || index >= ListModel.ListItems.Count)
            {
                return null;
            }

            return ListModel.ListItems[index].Item.Quote?.Change switch
            {
                > 0 => Color.Green,
                < 0 => Color.Red,
                _ => null,
            };
        }
    }

    private sealed class HitListBox : ListBox<StockSearchHit>
    {
        public HitListBox(IListBoxModel<StockSearchHit> listModel, IThemeService themeService)
            : base(listModel, themeService)
        {
            ListModel.TransformItem = (hit, _) => new ListItem<StockSearchHit>(hit)
            {
                Value = FormatHit(hit, (int)ViewSize.Width),
                IsEnabled = true,
            };
        }
    }

    internal static string FormatHit(StockSearchHit hit, int width)
    {
        var nameWidth = Math.Max(4, width - 1 - SymbolWidth - ExchangeWidth - 6);
        return Cut(" " + Cut(hit.Symbol, SymbolWidth - 1).PadRight(SymbolWidth) + Cut(hit.Name, nameWidth - 1).PadRight(nameWidth)
            + " " + Cut(hit.Exchange, ExchangeWidth).PadRight(ExchangeWidth) + " " + hit.Type, width);
    }

    private readonly IStockService _stockService;
    private readonly ConcurrentObservableCollection<StockRow> _rowItems = new();
    private readonly ConcurrentObservableCollection<StockSearchHit> _hitItems = new();
    private readonly StockListBox _stockListBox;
    private readonly HitListBox _hitListBox;
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
        Title = "Stocks";

        _stockListBox = new StockListBox(new ListBoxModel<StockRow>(_rowItems), themeService)
        {
            EmptyText = " The watchlist is empty — press a to add a stock.",
        };
        _hitListBox = new HitListBox(new ListBoxModel<StockSearchHit>(_hitItems), themeService)
        {
            EmptyText = " Nothing found.",
        };
        _inputBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            IsVisible = false,
            IsEnabled = false,
            CanFocus = false,
        };

        AddControl(_stockListBox);
        AddControl(_hitListBox);
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
            var listHeight = Math.Max(0, ih - 5);
            _stockListBox.Position = new Position(1, 3);
            _stockListBox.ViewSize = new Size(iw, listHeight);
            _hitListBox.Position = new Position(1, 3);
            _hitListBox.ViewSize = new Size(iw, listHeight);

            _inputBox.Position = new Position(1, ih);
            _inputBox.ViewSize = new Size(iw, 1);
            _inputBox.ScrollableModel.ViewSize = _inputBox.ViewSize;
        }

        // Children must have their Position/ViewSize assigned before base.OnInitializeLayout() recurses into them.
        base.OnInitializeLayout();
        _stockListBox.UpdateModel();
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
        _stockListBox.UpdateModel();
        ClampCursor(_stockListBox.ScrollableModel, rows.Count);
        StateHasChanged();
    }

    private static void ClampCursor(IScrollableModel scrollableModel, int count)
    {
        scrollableModel.CursorPosition.Y = count <= 0 ? 0 : Math.Clamp(scrollableModel.CursorPosition.Y, 0, count - 1);
        scrollableModel.ScrollPosition.Y = count <= 0 ? 0 : Math.Clamp(scrollableModel.ScrollPosition.Y, 0, count - 1);
    }

    private static T? CursorItem<T>(ListBox<T> box) where T : class
    {
        var items = box.ListModel.ListItems;
        var index = (int)box.ScrollableModel.CursorPosition.Y;
        return index >= 0 && index < items.Count ? items[index].Item : null;
    }

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
        SetMessage("Updating quotes…");
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
        _hitListBox.UpdateModel();
        ShowInput("Find stock by symbol or company name");
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
        _stockListBox.SetFocus();
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
        _hitListBox.IsVisible = _adding;
        _stockListBox.IsVisible = !_adding;
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
        SetMessage("Searching…");
        _ = Task.Run(async () =>
        {
            try
            {
                var hits = await _stockService.SearchAsync(query);
                _searchedQuery = query;
                _hitItems.ReplaceAll([.. hits]);
                _hitListBox.UpdateModel();
                ClampCursor(_hitListBox.ScrollableModel, hits.Count);
                SetMessage(hits.Count == 0
                    ? $"No stock found for \"{query}\". Enter again adds it as a symbol (exchange suffix: VOD.L)."
                    : $"{hits.Count} found — ↑↓ choose, Enter adds the chosen one");
            }
            catch (Exception exception)
            {
                // Any failure is said here: only the network ones were, so a bad reply left "Searching…" on the screen.
                SetMessage($"Error: search failed — {exception.Message}");
            }
            finally
            {
                _isBusy = false;
            }
        });
    }

    private void AddChosenHit()
    {
        if (CursorItem(_hitListBox) is { } hit)
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
        SetMessage($"Adding {symbol.ToUpperInvariant()}…");
        _ = Task.Run(async () =>
        {
            try
            {
                var stock = await _stockService.AddAsync(symbol, name);
                _closeAdding = true;
                SetMessage($"Added {stock.Symbol}");
            }
            catch (Exception exception)
            {
                // Any failure is said here, as for the search: a non-network one left "Adding…" on the screen.
                SetMessage($"Error: {exception.Message}");
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
        if (CursorItem(_stockListBox) is not { } row)
        {
            return;
        }

        var symbol = row.Stock.Symbol;
        _confirmPrompt = $"Remove {symbol} from the list?  y:yes  any other key:no";
        _confirmAction = () =>
        {
            _ = Task.Run(() =>
            {
                SetMessage(_stockService.Remove(symbol) ? $"Removed {symbol}" : $"{symbol} is not on the list");
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
            yield return ("Y", "Confirm");
            yield return ("Any other key", "Cancel");
            yield break;
        }

        if (_adding)
        {
            yield return ("Enter", "Search, or add the chosen hit");
            yield return ("Up/Down", "Choose a hit");
            yield return ("Esc", "Back to the list");
            yield break;
        }

        yield return ("A", "Add a stock");
        yield return ("D/Delete", "Remove the stock");
        yield return ("R", "Refresh the quotes");
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
                    return await _hitListBox.HandleKeyEventAsync(keyEvent);
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

    internal const string KeyHints = "a:add  d/Del:remove  r:refresh  Esc:close";
    internal const string AddKeyHints = "Enter:search/add  ↑↓:choose  Esc:back";

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
        var headings = _adding
            ? FormatHitHeadings(iw)
            : FormatColumns("Symbol", "Name", "Price", "Change", "Change %", "Exchange", iw);
        DrawSectionHeader(buffer, ix, iy + 1, iw, headings.TrimStart(), _adding ? _hitListBox.IsFocused : _stockListBox.IsFocused, colorScheme);

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

    private static string FormatHitHeadings(int width)
    {
        var nameWidth = Math.Max(4, width - 1 - SymbolWidth - ExchangeWidth - 6);
        return Cut(" " + "Symbol".PadRight(SymbolWidth) + "Company".PadRight(nameWidth) + " " + "Exchange".PadRight(ExchangeWidth) + " Type", width);
    }

    private void DrawHeader(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        var updated = _stockService.LastUpdated;
        var text = _adding
            ? "Stocks — add a stock"
            : $"Stocks — {_rowItems.Count} on the list" + (updated is { } time ? $"   live, updated {time:HH:mm:ss}" : "   waiting for the first quotes…");
        WriteLine(buffer, x, y, w, text, Color.Cyan, colorScheme.Background);
    }

    /// <summary>The cursor stock in more detail: the day's and the year's range, volume and last trade.</summary>
    private void DrawDetails(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        var text = string.Empty;
        if (!_adding && CursorItem(_stockListBox) is { Quote: { } quote })
        {
            var parts = new List<string> { quote.Name };
            if (quote is { DayLow: { } dayLow, DayHigh: { } dayHigh })
            {
                parts.Add($"day {FormatPrice(dayLow)}–{FormatPrice(dayHigh)}");
            }

            if (quote is { YearLow: { } yearLow, YearHigh: { } yearHigh })
            {
                var position = yearHigh > yearLow ? (double)((quote.Price - yearLow) / (yearHigh - yearLow)) : 0;
                parts.Add($"52w {FormatPrice(yearLow)} ▕{BarChart.BuildBar(position, 8)}▏ {FormatPrice(yearHigh)}");
            }

            parts.Add($"prev close {FormatPrice(quote.PreviousClose)} {quote.Currency}");
            if (quote.Volume is { } volume)
            {
                parts.Add($"vol {volume.ToString("N0", CultureInfo.InvariantCulture)}");
            }

            if (quote.MarketTime is { } last)
            {
                parts.Add($"last trade {last:yyyy-MM-dd HH:mm}");
            }

            text = " " + string.Join("   ", parts);
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
        var text = _adding
            ? " " + _message
            : $" {up} up   {down} down   {quotes.Count - up - down} unchanged" + (_rowItems.Count > quotes.Count ? $"   {_rowItems.Count - quotes.Count} not quoted" : string.Empty);
        WriteLine(buffer, x, y, w, text, colorScheme.Foreground, colorScheme.Background);
    }

    private void DrawFooter(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        if (_confirmAction is not null)
        {
            WriteLine(buffer, x, y, w, _confirmPrompt, Color.Red, colorScheme.Background);
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
    internal int StockRowsShownForTest => _stockListBox.Count;
    internal int HitRowsShownForTest => _hitListBox.Count;
    internal IReadOnlyList<StockSearchHit> HitsForTest => [.. _hitItems.AsSnapshot()];
    internal TextBox InputBoxForTest => _inputBox;

    #endregion
}

using System.Globalization;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Controls.Charts;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Dialogs.Stocks;
using iTerminal.Core.Services.Stocks;

namespace iTerminal.Core.Dialogs.Terminal;

/// <summary>
/// The market terminal (Alt+Shift+F7): a full-screen, command-line view of the markets in the style of a trading
/// terminal. Type a symbol or a company name on the command line and press Enter, and the security screen below the
/// monitor fills with its price, the day's and the year's range and the last trade. The monitor is the watchlist,
/// live as the stock browser's is; a ticker tape across the top shows every watched stock at a glance. F6 puts the
/// security on the watchlist, or takes it off, and a security that is watched follows the watchlist's live quote.
///
/// <para>Nothing here waits on the network. A lookup runs on a background task and parks its result; the next idle
/// pass applies it on the UI thread, and only the newest lookup may land (an older one is dropped).</para>
/// </summary>
public class MarketTerminalDialog : SplitPaneDialog
{
    /// <summary>Rows at the bottom: the security heading, its three lines of detail and the status line.</summary>
    private const int SecurityBlockHeight = 5;

    /// <summary>The chart's plot rows; the block above the security is one heading row and these.</summary>
    private const int ChartRows = 5;
    private const int ChartBlockHeight = ChartRows + 1;

    /// <summary>The columns the chart's price labels take on the right.</summary>
    private const int ChartLabelWidth = 10;

    /// <summary>The rows the monitor needs above the security block: the command line, the tape and the monitor heading.</summary>
    private const int MonitorTopRows = 3;

    private readonly IStockService _stockService;
    private readonly ConcurrentObservableCollection<StockBrowserDialog.StockRow> _rowItems = new();
    private readonly Table<StockBrowserDialog.StockRow> _listBox;
    private readonly TextBox _inputBox;
    private readonly Lock _landGate = new();
    private readonly CancellationTokenSource _cancellation = new();

    private IDisposable? _watch;
    private StockQuote? _security;
    private Lookup? _pending;
    private int _generation;
    private volatile string _message = string.Empty;
    private volatile bool _isBusy;

    private IReadOnlyList<StockBar> _history = [];
    private bool _historyLoaded;
    private string? _historySymbol;
    private StockHistoryRange _range = StockHistoryRange.OneYear;
    private int _historyGeneration;
    private IReadOnlyList<StockBar>? _pendingHistory;

    /// <summary>The result of a lookup, parked until the UI thread applies it. A null quote means the lookup found nothing.</summary>
    private sealed record Lookup(int Generation, StockQuote? Quote, string Message);

    internal static string KeyHints => UiText.Get("Hints.MarketTerminal", "Enter:look up or show  ↑↓:monitor  F5:refresh  F6:watch  F8:period  Esc:close");

    public MarketTerminalDialog(
        IStockService stockService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(MarketTerminalDialog), themeService)
    {
        _stockService = stockService;
        Title = UiText.Get("Dialog.MarketTerminal", "Market Terminal");

        _listBox = new Table<StockBrowserDialog.StockRow>(_rowItems, themeService)
        {
            EmptyText = UiText.Get("Empty.Watchlist", " The watchlist is empty — add stocks in the stock browser (Alt+F7)."),
            // The same colouring as the stock browser: a rise is green, a fall red.
            RowForeground = row => row.Quote?.Change switch
            {
                > 0 => Color.Green,
                < 0 => Color.Red,
                _ => null,
            },
        };
        _inputBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("Market.Prompt", "Look up  ›"),
            Placeholder = UiText.Get("Market.Placeholder", "AAPL, VOD.L or a company name, then Enter"),
        };

        AddControl(_inputBox);
        AddControl(_listBox);
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        _stockService.Changed += OnStocksChanged;
        _watch ??= _stockService.Watch();
        RecomputeRows();
        _inputBox.SetFocus();
    }

    public override void Close()
    {
        _stockService.Changed -= OnStocksChanged;
        _watch?.Dispose();
        _watch = null;
        _cancellation.Cancel();
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
            // Interior rows: 0 command line, 1 tape, 2 monitor heading, then the monitor, the security heading
            // (row ih-5), its lines (ih-4 to ih-2) and the status line (ih-1).
            _inputBox.Position = new Position(1, 1);
            _inputBox.ViewSize = new Size(iw, 1);
            _inputBox.ScrollableModel.ViewSize = _inputBox.ViewSize;

            // Interior rows, from the bottom: the status line, the security's three lines and heading, then the chart
            // (its heading and plot rows) and the monitor above it.
            // The table's first row is its heading, which the monitor heading used to be, so the entries start below it as before.
            var monitorRows = Math.Max(0, ih - MonitorTopRows - SecurityBlockHeight - ChartBlockHeight);
            _listBox.Position = new Position(1, MonitorTopRows);
            _listBox.ViewSize = new Size(iw, monitorRows + 1);
            _listBox.ScrollableModel.ViewSize = new Size(iw, monitorRows);
            _listBox.Columns.Clear();
            _listBox.Columns.AddRange(StockBrowserDialog.WatchlistColumns(iw));
        }

        base.OnInitializeLayout();
    }

    #region Monitor

    private void OnStocksChanged(object? sender, EventArgs e) => RecomputeRows();

    /// <summary>Re-reads the watchlist and its quotes, in memory, and keeps the cursor where it was.</summary>
    private void RecomputeRows()
    {
        var rows = _stockService.Watchlist
            .Select(stock => new StockBrowserDialog.StockRow(stock, _stockService.GetQuote(stock.Symbol)))
            .ToList();
        _rowItems.ReplaceAll(rows);
        var scrollable = _listBox.ScrollableModel;
        scrollable.CursorPosition.Y = rows.Count <= 0 ? 0 : Math.Clamp(scrollable.CursorPosition.Y, 0, rows.Count - 1);
        scrollable.ScrollPosition.Y = rows.Count <= 0 ? 0 : Math.Clamp(scrollable.ScrollPosition.Y, 0, rows.Count - 1);
        StateHasChanged();
    }

    /// <summary>The monitor's cursor row, or null when the monitor is empty.</summary>
    private StockBrowserDialog.StockRow? CursorRow()
    {
        return _listBox.SelectedRow;
    }

    private void SetMessage(string message)
    {
        _message = message;
        StateHasChanged();
    }

    #endregion

    #region Lookups

    /// <summary>Enter on the command line looks the text up. Enter on an empty line shows the monitor's cursor row.</summary>
    private void Submit()
    {
        var query = _inputBox.Text.Trim();
        if (query.Length == 0)
        {
            if (CursorRow() is { Quote: { } quote })
            {
                ShowSecurity(quote);
                SetMessage(string.Empty);
            }
            else
            {
                SetMessage(UiText.Get("Market.NoSecurity", "Type a symbol or company name and press Enter."));
            }

            StateHasChanged();
            return;
        }

        if (_isBusy)
        {
            return;
        }

        StartLookup(query);
    }

    private void StartLookup(string query)
    {
        _isBusy = true;
        var generation = ++_generation;
        SetMessage(string.Format(UiText.Get("Market.Looking", "Looking up {0}…"), query.ToUpperInvariant()));
        _ = Task.Run(async () =>
        {
            try
            {
                // The symbol first; a company name has no quote of its own, so its best search hit is used.
                var quote = await _stockService.QuoteAsync(query, _cancellation.Token);
                if (quote is null)
                {
                    var hits = await _stockService.SearchAsync(query, _cancellation.Token);
                    if (hits.Count > 0)
                    {
                        quote = await _stockService.QuoteAsync(hits[0].Symbol, _cancellation.Token);
                    }
                }

                Park(new Lookup(generation, quote, quote is null ? string.Format(UiText.Get("Market.NotFound", "No quote for {0}. Check the symbol; other exchanges add a suffix, such as VOD.L."), query.ToUpperInvariant()) : string.Empty));
            }
            catch (OperationCanceledException)
            {
                // The dialog closed while the lookup was out: nothing to show.
            }
            catch (Exception exception)
            {
                Park(new Lookup(generation, null, string.Format(UiText.Get("Msg.ErrorPrefix", "Error: {0}"), exception.Message)));
            }
            finally
            {
                _isBusy = false;
            }
        });
    }

    /// <summary>F5: refreshes the monitor and the security on screen.</summary>
    private void RefreshNow()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        var generation = _generation;
        var symbol = _security?.Symbol;
        SetMessage(UiText.Get("Msg.UpdatingQuotes", "Updating quotes…"));
        _ = Task.Run(async () =>
        {
            try
            {
                await _stockService.RefreshAsync(_cancellation.Token);
                if (symbol is not null && await _stockService.QuoteAsync(symbol, _cancellation.Token) is { } refreshed)
                {
                    Park(new Lookup(generation, refreshed, string.Empty));
                }

                SetMessage(_stockService.LastError);
            }
            catch (OperationCanceledException)
            {
                // Closed mid-refresh.
            }
            catch (Exception exception)
            {
                SetMessage(string.Format(UiText.Get("Msg.ErrorPrefix", "Error: {0}"), exception.Message));
            }
            finally
            {
                _isBusy = false;
            }
        });
    }

    /// <summary>Only the newest lookup may park its result: an older one that lands late is dropped.</summary>
    private void Park(Lookup lookup)
    {
        lock (_landGate)
        {
            if (lookup.Generation == _generation)
            {
                _pending = lookup;
            }
        }
    }

    /// <summary>Applies a parked lookup on the UI thread; the tree is not the worker's to change.</summary>
    private void ApplyPending()
    {
        Lookup? lookup;
        lock (_landGate)
        {
            lookup = _pending;
            _pending = null;
        }

        if (lookup is null)
        {
            return;
        }

        if (lookup.Quote is { } quote)
        {
            ShowSecurity(quote);
            _inputBox.Text = string.Empty;
        }

        SetMessage(lookup.Message);
    }

    /// <summary>Puts a security on the screen and asks for its history; a different stock starts with an empty chart.</summary>
    private void ShowSecurity(StockQuote quote)
    {
        _security = quote;
        RequestHistory();
    }

    public override void OnIdle()
    {
        base.OnIdle();
        ApplyPending();
        ApplyPendingHistory();
        FollowWatchedSecurity();
    }

    #endregion

    #region History

    /// <summary>The history a request parked; the newest request is the only one that may land.</summary>
    private void ParkHistory(int generation, IReadOnlyList<StockBar> bars)
    {
        lock (_landGate)
        {
            if (generation == _historyGeneration)
            {
                _pendingHistory = bars;
            }
        }
    }

    /// <summary>
    /// Asks for the security's history at the current period, on a background task. A refresh of the same stock keeps
    /// the chart on screen until the newer answer lands; a different stock, or another period, starts with an empty one.
    /// </summary>
    private void RequestHistory()
    {
        if (_security is not { } shown)
        {
            return;
        }

        var symbol = shown.Symbol;
        if (!string.Equals(symbol, _historySymbol, StringComparison.OrdinalIgnoreCase))
        {
            _history = [];
            _historyLoaded = false;
            _historySymbol = symbol;
        }

        int generation;
        lock (_landGate)
        {
            generation = ++_historyGeneration;
            _pendingHistory = null;
        }

        var range = _range;
        _ = Task.Run(async () =>
        {
            IReadOnlyList<StockBar> bars = [];
            try
            {
                bars = await _stockService.HistoryAsync(symbol, range, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // No chart: the screen says the security has no history.
            }

            ParkHistory(generation, bars);
        });
    }

    /// <summary>Applies a parked history on the UI thread.</summary>
    private void ApplyPendingHistory()
    {
        IReadOnlyList<StockBar>? bars;
        lock (_landGate)
        {
            bars = _pendingHistory;
            _pendingHistory = null;
        }

        if (bars is null)
        {
            return;
        }

        _history = bars;
        _historyLoaded = true;
        StateHasChanged();
    }

    /// <summary>F8: the next period, one month to five years, and the chart is drawn again for it.</summary>
    private void CycleRange()
    {
        _range = _range == StockHistoryRange.FiveYears ? StockHistoryRange.OneMonth : _range + 1;
        _historySymbol = null;
        RequestHistory();
        StateHasChanged();
    }

    internal static string RangeLabel(StockHistoryRange range) => range switch
    {
        StockHistoryRange.OneMonth => "1M",
        StockHistoryRange.ThreeMonths => "3M",
        StockHistoryRange.OneYear => "1Y",
        StockHistoryRange.FiveYears => "5Y",
        _ => "1Y",
    };

    /// <summary>The chart's heading: the period, the first and last close, and how far the price moved over the period.</summary>
    internal static string FormatChartHeading(StockHistoryRange range, IReadOnlyList<StockBar> bars, int width)
    {
        var first = bars[0].Close;
        var last = bars[^1].Close;
        var percent = first == 0 ? 0 : (last - first) / first * 100m;
        return Cut(string.Format(UiText.Get("Market.ChartHeading", "Chart {0}   {1} to {2}   {3} over the period"),
            RangeLabel(range), StockBrowserDialog.FormatPrice(first), StockBrowserDialog.FormatPrice(last), StockBrowserDialog.FormatPercent(percent)), width);
    }

    /// <summary>A watched security takes the watchlist's quote whenever the loop refreshes it, so its screen stays live.</summary>
    private void FollowWatchedSecurity()
    {
        if (_security is { } shown && _stockService.GetQuote(shown.Symbol) is { } latest && latest != shown)
        {
            _security = latest;
            StateHasChanged();
        }
    }

    #endregion

    #region Watchlist

    private bool IsWatched(string symbol) =>
        _stockService.Watchlist.Any(stock => string.Equals(stock.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// F6: puts the security on the watchlist, or takes it off when it is already there. Adding asks the source first,
    /// so it runs on a background task like a lookup; removing is in memory and answers at once.
    /// </summary>
    private void ToggleWatch()
    {
        if (_security is not { } shown)
        {
            SetMessage(UiText.Get("Market.NoSecurityToWatch", "Look up a stock first, then press F6 to watch it."));
            return;
        }

        if (IsWatched(shown.Symbol))
        {
            _stockService.Remove(shown.Symbol);
            SetMessage(string.Format(UiText.Get("Stocks.Removed", "Removed {0}"), shown.Symbol));
            return;
        }

        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        var symbol = shown.Symbol;
        SetMessage(string.Format(UiText.Get("Msg.AddingSymbol", "Adding {0}…"), symbol));
        _ = Task.Run(async () =>
        {
            try
            {
                await _stockService.AddAsync(symbol, cancellationToken: _cancellation.Token);
                SetMessage(string.Format(UiText.Get("Market.Added", "Added {0} to the watchlist"), symbol));
            }
            catch (OperationCanceledException)
            {
                // Closed mid-add.
            }
            catch (InvalidOperationException exception)
            {
                // The service words these for the user (already listed, no quote).
                SetMessage(exception.Message);
            }
            catch (Exception exception)
            {
                SetMessage(string.Format(UiText.Get("Msg.ErrorPrefix", "Error: {0}"), exception.Message));
            }
            finally
            {
                _isBusy = false;
            }
        });
    }

    #endregion

    #region Input

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", UiText.Get("Market.LookUp", "Look up the symbol, or show the chosen row"));
        yield return ("Up/Down", UiText.Get("DialogHint.ChooseHit", "Choose a hit"));
        yield return ("F5", UiText.Get("DialogHint.RefreshQuotes", "Refresh the quotes"));
        yield return ("F6", UiText.Get("DialogHint.WatchSecurity", "Add or remove the security from the watchlist"));
        yield return ("F8", UiText.Get("DialogHint.ChartPeriod", "Change the chart's period (1M, 3M, 1Y, 5Y)"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        ApplyPending();

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Enter:
                Submit();
                return true;
            case ConsoleKey.UpArrow:
            case ConsoleKey.DownArrow:
            case ConsoleKey.PageUp:
            case ConsoleKey.PageDown:
                // The command line keeps the other keys for typing; the monitor is driven from here.
                return await _listBox.HandleKeyEventAsync(keyEvent);
            case ConsoleKey.F5:
                RefreshNow();
                RequestHistory();
                return true;
            case ConsoleKey.F6:
                ToggleWatch();
                return true;
            case ConsoleKey.F8:
                CycleRange();
                return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    #endregion

    #region Formatting

    internal static string Cut(string text, int width) => text.Length <= width ? text : text[..Math.Max(0, width)];

    /// <summary>The ticker tape: every quoted stock as "SYMBOL price change %", in the order of the watchlist.</summary>
    internal static string FormatTape(IEnumerable<StockQuote> quotes, int width, string waiting)
    {
        var items = quotes.Select(quote => $"{quote.Symbol} {StockBrowserDialog.FormatPrice(quote.Price)} {StockBrowserDialog.FormatPercent(quote.ChangePercent)}").ToList();
        var text = items.Count == 0 ? " " + waiting : " " + string.Join("   ", items);
        return Cut(text, width);
    }

    /// <summary>The security's name line: symbol, name, exchange and currency.</summary>
    internal static string FormatSecurityTitle(StockQuote quote, int width) =>
        Cut($" {quote.Symbol}  {quote.Name}   {quote.Exchange}   {quote.Currency}", width);

    /// <summary>The security's price line: the last price, the change in value and in percent, the previous close and the volume.</summary>
    internal static string FormatSecurityPrice(StockQuote quote, int width)
    {
        var parts = new List<string>
        {
            $"{StockBrowserDialog.FormatPrice(quote.Price)} {quote.Currency}",
            $"{StockBrowserDialog.FormatChange(quote.Change)} ({StockBrowserDialog.FormatPercent(quote.ChangePercent)})",
            string.Format(UiText.Get("Stocks.PrevClose", "prev close {0} {1}"), StockBrowserDialog.FormatPrice(quote.PreviousClose), quote.Currency),
        };
        if (quote.Volume is { } volume)
        {
            parts.Add(string.Format(UiText.Get("Stocks.Volume", "vol {0}"), volume.ToString("N0", CultureInfo.InvariantCulture)));
        }

        return Cut(" " + string.Join("   ", parts), width);
    }

    /// <summary>The security's range line: the day's range, where the price sits in the year's range, and the last trade.</summary>
    internal static string FormatSecurityRange(StockQuote quote, int width)
    {
        var parts = new List<string>();
        if (quote is { DayLow: { } dayLow, DayHigh: { } dayHigh })
        {
            parts.Add(string.Format(UiText.Get("Stocks.Day", "day {0}–{1}"), StockBrowserDialog.FormatPrice(dayLow), StockBrowserDialog.FormatPrice(dayHigh)));
        }

        if (quote is { YearLow: { } yearLow, YearHigh: { } yearHigh })
        {
            var position = yearHigh > yearLow ? (double)((quote.Price - yearLow) / (yearHigh - yearLow)) : 0;
            parts.Add(string.Format(UiText.Get("Stocks.Year", "52w {0} ▕{1}▏ {2}"), StockBrowserDialog.FormatPrice(yearLow), BarChart.BuildBar(position, 8), StockBrowserDialog.FormatPrice(yearHigh)));
        }

        if (quote.MarketTime is { } last)
        {
            parts.Add(string.Format(UiText.Get("Stocks.LastTrade", "last trade {0}"), last.ToString("yyyy-MM-dd HH:mm")));
        }

        return Cut(" " + string.Join("   ", parts), width);
    }

    #endregion

    #region Draw

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

        var quotes = _rowItems.AsSnapshot().Select(row => row.Quote).OfType<StockQuote>().ToList();
        if (ih >= 2)
        {
            WriteLine(buffer, ix, iy + 1, iw, FormatTape(quotes, iw, UiText.Get("Stocks.Waiting", "waiting for the first quotes…")), colorScheme.Foreground, colorScheme.Background);
        }

        if (ih >= MonitorTopRows + SecurityBlockHeight + ChartBlockHeight + 1)
        {
            DrawChart(buffer, ix, iy + ih - SecurityBlockHeight - ChartBlockHeight, iw, colorScheme);
        }

        if (ih >= MonitorTopRows + SecurityBlockHeight + 1)
        {
            var heading = UiText.Get("Market.Security", "Security") + (_security is { } shown
                ? "  " + shown.Symbol + (IsWatched(shown.Symbol) ? "  · " + UiText.Get("Market.Watched", "watched") : string.Empty)
                : string.Empty);
            DrawSectionHeader(buffer, ix, iy + ih - SecurityBlockHeight, iw, heading, false, colorScheme);
            DrawSecurity(buffer, ix, iy + ih - SecurityBlockHeight + 1, iw, colorScheme);
        }

        if (ih >= 1)
        {
            var status = _message.Length > 0 ? _message : _stockService.LastError;
            var foreground = status.StartsWith("Error", StringComparison.Ordinal) || _message.Length == 0 && status.Length > 0
                ? colorScheme.Error
                : colorScheme.Foreground;
            DrawStatusAndHints(buffer, ix, iy + ih - 1, iw, status, foreground, KeyHints, colorScheme);
        }
    }

    /// <summary>
    /// The chart block: a heading, then the plot with the highest and lowest close on its right. The plot is green when
    /// the period ends above where it began. Every row is written, so a block with less to show is padded, not left stale.
    /// </summary>
    private void DrawChart(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        var heading = UiText.Get("Market.Chart", "Chart") + "  " + RangeLabel(_range);
        var lines = Enumerable.Repeat((Text: string.Empty, Foreground: colorScheme.Foreground), ChartRows).ToArray();
        if (_security is null)
        {
            lines[0] = (" " + UiText.Get("Market.NoSecurity", "Type a symbol or company name and press Enter."), colorScheme.DisabledForeground);
        }
        else if (!_historyLoaded)
        {
            lines[0] = (" " + UiText.Get("Market.ChartLoading", "loading the history…"), colorScheme.DisabledForeground);
        }
        else if (_history.Count == 0)
        {
            lines[0] = (" " + UiText.Get("Market.ChartNone", "no price history for this security"), colorScheme.DisabledForeground);
        }
        else
        {
            heading = FormatChartHeading(_range, _history, w).TrimStart();
            var plotWidth = Math.Max(1, w - ChartLabelWidth - 2);
            var plot = LineChart.Render(_history.Select(bar => (double)bar.Close).ToList(), plotWidth, ChartRows);
            var color = _history[^1].Close >= _history[0].Close ? Color.Green : Color.Red;
            for (var row = 0; row < ChartRows; row++)
            {
                var label = row switch
                {
                    0 => StockBrowserDialog.FormatPrice(_history.Max(bar => bar.Close)),
                    _ when row == ChartRows - 1 => StockBrowserDialog.FormatPrice(_history.Min(bar => bar.Close)),
                    _ => string.Empty,
                };
                lines[row] = (" " + plot[row].PadRight(plotWidth) + " " + label, color);
            }
        }

        DrawSectionHeader(buffer, x, y, w, heading, false, colorScheme);
        for (var row = 0; row < ChartRows; row++)
        {
            WriteLine(buffer, x, y + 1 + row, w, lines[row].Text, lines[row].Foreground, colorScheme.Background);
        }
    }

    /// <summary>The three lines under the security heading. Without a security, the first says how to get one.</summary>
    private void DrawSecurity(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        if (_security is not { } quote)
        {
            WriteLine(buffer, x, y, w, " " + UiText.Get("Market.NoSecurity", "Type a symbol or company name and press Enter."), colorScheme.DisabledForeground, colorScheme.Background);
            return;
        }

        WriteLine(buffer, x, y, w, FormatSecurityTitle(quote, w), colorScheme.Foreground, colorScheme.Background);
        WriteLine(buffer, x, y + 1, w, FormatSecurityPrice(quote, w), quote.Change switch
        {
            > 0 => Color.Green,
            < 0 => Color.Red,
            _ => colorScheme.Foreground,
        }, colorScheme.Background);
        WriteLine(buffer, x, y + 2, w, FormatSecurityRange(quote, w), colorScheme.DisabledForeground, colorScheme.Background);
    }

    #endregion

    #region Test hooks

    internal string MessageForTest => _message;
    internal StockQuote? SecurityForTest => _security;
    internal TextBox InputBoxForTest => _inputBox;
    internal Table<StockBrowserDialog.StockRow> ListBoxForTest => _listBox;
    internal StockQuote? CursorQuoteForTest => CursorRow()?.Quote;
    internal IReadOnlyList<StockBar> HistoryForTest => _history;
    internal bool HistoryLoadedForTest => _historyLoaded;
    internal StockHistoryRange RangeForTest => _range;

    /// <summary>How many monitor rows the list has actually taken up. The rows are filled on a worker, and the list is rebuilt after them.</summary>
    internal int MonitorRowsShownForTest => _rowItems.Count;

    #endregion
}

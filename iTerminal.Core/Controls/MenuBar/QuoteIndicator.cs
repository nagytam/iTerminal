using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Services;

namespace iTerminal.Core.Controls.MenuBar;

/// <summary>
/// One instrument's latest quote in the menu bar: the price, an arrow and a green or red band for
/// the direction of the last move, and a "[cached]" marker while the value is the one from the last
/// session rather than a fresh fetch.
/// </summary>
public class QuoteIndicator(string instrumentName, OnlineQuoteService onlineQuoteService, IThemeService themeService) : iConsole.Controls.Control(themeService)
{
    /// <summary>The marker both menu-bar indicators put in front of a value from the cache.</summary>
    internal const string CachedMarker = "[cached]";

    private string _instrumentName { get; set; } = instrumentName;
    private OnlineQuoteService _onlineQuoteService { get; set; } = onlineQuoteService;

    private Instrument? GetInstrument()
    {
        if (_onlineQuoteService.Instruments.TryGetValue(_instrumentName, out var instrument))
        {
            return instrument;
        }

        var symbolPrefix = _instrumentName switch
        {
            "Bitcoin" => "BTC/",
            "Ethereum" => "ETH/",
            _ => null,
        };

        if (symbolPrefix is not null)
        {
            instrument = _onlineQuoteService.Instruments.Values.FirstOrDefault(i =>
                i.Currency.StartsWith(symbolPrefix, StringComparison.OrdinalIgnoreCase));
            if (instrument is not null)
            {
                return instrument;
            }
        }

        return _onlineQuoteService.Instruments.Values.FirstOrDefault(i =>
            i.Name.Equals(_instrumentName, StringComparison.OrdinalIgnoreCase));
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        StartAutoRefresh();
    }

    /// <summary>
    /// The text and band colour to draw. The text is fitted to <paramref name="width"/> here, with
    /// the <em>price</em> kept: the pair name goes first when there is not room for everything, since
    /// the price and its arrow are what the indicator is for — and the arrow, written last, is what
    /// the buffer used to clip.
    /// </summary>
    internal (string Text, Color Background) Describe(int width)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        var instrument = GetInstrument();

        // While nothing has arrived, name what is loading: with two indicators side by side, two
        // identical "Fetch quote" strings said nothing about which was which.
        if (instrument is null)
        {
            // Once an attempt has failed and none is running, say so rather than spin.
            var nothingComing = _onlineQuoteService.LastRefreshFailed && !_onlineQuoteService.IsRefreshing;
            var status = nothingComing ? "unavailable" : ProgressChar();
            return (Fit($"{_instrumentName} {status}", width), colorScheme.Background);
        }

        var lastValue = instrument.InstrumentValues.FirstOrDefault();
        if (lastValue is null)
        {
            return (Fit($"{instrument.Name} {ProgressChar()}", width), colorScheme.Background);
        }

        var background = colorScheme.Background;
        var direction = string.Empty;
        var previousValue = instrument.InstrumentValues.FirstOrDefault(v => v.Value != lastValue.Value);
        if (previousValue is not null)
        {
            if (lastValue.Value > previousValue.Value)
            {
                background = Color.Green;
                direction = Border.ArrowUp.ToString();
            }
            else
            {
                background = Color.Red;
                direction = Border.ArrowDown.ToString();
            }
        }

        var isCached = _onlineQuoteService.IsFromCache;
        var cached = isCached
            ? CachedMarker + (_onlineQuoteService.IsRefreshing ? $"{ProgressChar()} " : " ")
            : string.Empty;

        // Grouped, two decimals: "82953.0000" read as a phone number more than a price.
        var price = lastValue.Value.ToString("N2") + direction;

        // Longest first; the first that fits wins. The slots in the menu bar are 17-18 columns, so
        // "[cached] BTC/USD 82,953.00↑" never fits — a cached value then keeps a one-character "*".
        string[] candidates = isCached
            ? [$"{cached}{instrument.Currency} {price}", $"{cached}{price}", $"*{instrument.Currency} {price}", $"*{price}"]
            : [$"{instrument.Currency} {price}", price];
        var text = candidates.FirstOrDefault(c => c.Length <= width) ?? candidates[^1];

        return (Fit(text, width), background);
    }

    private static string Fit(string text, int width) =>
        width <= 0 ? string.Empty : (text.Length > width ? text[^width..] : text).ExpandRight(width);

    public override void Draw(IBuffer buffer)
    {
        var (text, background) = Describe((int)ViewSize.Width);
        var quote = new ConxelString(text, ThemeService.CurrentColorScheme.Foreground, background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, quote);
    }
}

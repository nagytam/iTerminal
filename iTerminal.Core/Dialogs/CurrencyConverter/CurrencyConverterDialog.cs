using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Currency;
using System.Globalization;

namespace iTerminal.Core.Dialogs.CurrencyConverter;

/// <summary>
/// The currency converter: an amount to type and a currency to convert it from and into. The rates are the European
/// Central Bank's daily reference rates, downloaded when the dialog opens if the ones in hand are stale (and the last
/// ones are kept for working offline). F5 downloads them again, F6 swaps the currencies, Esc closes.
///
/// <para>Nothing here waits on the network: the download runs on a background task and parks its outcome until the
/// next idle pass applies it on the UI thread. Only the newest download may land, and closing cancels the one in flight.</para>
/// </summary>
public class CurrencyConverterDialog : SplitPaneDialog
{
    internal const string KeyHints = "Tab:next field  Up/Down:choose  F5:refresh rates  F6:swap  Esc:close";

    internal const string AmountKey = "CurrencyConverter.Amount";
    internal const string FromKey = "CurrencyConverter.From";
    internal const string ToKey = "CurrencyConverter.To";

    internal sealed record CurrencyChoice(string Code)
    {
        public string Display => $"{Code}  {CurrencyNames.NameOf(Code)}";
    }

    private sealed record Outcome(int Generation, CurrencyRates? Rates, string? Error);

    private readonly ICurrencyService _service;
    private readonly ISettingsService _settings;
    private readonly TextBox _amount;
    private readonly ComboBox<CurrencyChoice> _from;
    private readonly ComboBox<CurrencyChoice> _to;

    private CancellationTokenSource? _cancellation;
    private Outcome? _pending;
    private int _generation;
    private string _status = string.Empty;
    private bool _statusIsError;
    private volatile bool _busy;

    public CurrencyConverterDialog(
        ICurrencyService service,
        ISettingsService settings,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(CurrencyConverterDialog), themeService)
    {
        _service = service;
        _settings = settings;
        Title = iConsole.Localization.UiText.Get("Menu.CurrencyConverter", "Currency Converter");

        _amount = new TextBox(beepService, dateTimeProviderService, themeService) { Label = UiText.Get("Field.Amount", "Amount"), Text = "1" };
        _from = new ComboBox<CurrencyChoice>(themeService) { Label = UiText.Get("Field.From", "From"), DisplaySelector = choice => choice.Display, MaxDropDownRows = 10 };
        _to = new ComboBox<CurrencyChoice>(themeService) { Label = iConsole.Localization.UiText.Get("Label.To", "To"), DisplaySelector = choice => choice.Display, MaxDropDownRows = 10 };

        AddControl(_amount);
        AddControl(_from);
        AddControl(_to);
    }

    #region Test seams

    internal TextBox AmountBox => _amount;
    internal ComboBox<CurrencyChoice> FromBox => _from;
    internal ComboBox<CurrencyChoice> ToBox => _to;
    internal string Status => _status;
    internal bool IsBusy => _busy;

    /// <summary>The download the dialog last started; nothing awaits it in the dialog, so a test waits on it instead of polling.</summary>
    internal Task RequestTask { get; private set; } = Task.CompletedTask;

    /// <summary>Applies the pending outcome now; the idle pass does the same in the running app.</summary>
    internal void ApplyPendingForTest() => OnIdle();

    #endregion

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        string? amount = _settings.Get(AmountKey);
        _amount.Text = string.IsNullOrWhiteSpace(amount) ? "1" : amount;
        PopulateCurrencies(_settings.Get(FromKey) ?? "EUR", _settings.Get(ToKey) ?? "USD");

        if (_service.Current is { } rates)
        {
            _status = $"Rates of {rates.Date:yyyy-MM-dd}.";
        }

        if (_service.IsStale)
        {
            StartRefresh();
        }
    }

    public override void Close()
    {
        _settings.Set(AmountKey, _amount.Text);
        if (_from.SelectedItem is { } from)
        {
            _settings.Set(FromKey, from.Code);
        }

        if (_to.SelectedItem is { } to)
        {
            _settings.Set(ToKey, to.Code);
        }

        // Cancelled, not awaited: the download ends on its own, and a late outcome is dropped by its generation.
        _cancellation?.Cancel();
        _generation++;
        base.Close();
    }

    /// <summary>Fills both pickers from the rates in hand, keeping (or choosing) the given currencies.</summary>
    private void PopulateCurrencies(string fromCode, string toCode)
    {
        IReadOnlyList<string> codes = _service.Current?.Codes ?? [];
        CurrencyChoice[] choices = [.. codes.Select(code => new CurrencyChoice(code))];
        _from.SetItems(choices);
        _to.SetItems(choices);
        _from.SelectedIndex = Math.Max(0, IndexOf(choices, fromCode));
        _to.SelectedIndex = Math.Max(0, IndexOf(choices, toCode));
    }

    private static int IndexOf(CurrencyChoice[] choices, string code) =>
        Array.FindIndex(choices, choice => string.Equals(choice.Code, code, StringComparison.OrdinalIgnoreCase));

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.8);
        }

        int iw = (int)ViewSize.Width - 2;
        int ih = (int)ViewSize.Height - 2;
        if (iw > 0 && ih > 0)
        {
            int boxWidth = Math.Max(10, (iw - 2) / 3);
            _amount.Position = new Position(1, 1);
            _amount.ViewSize = new Size(boxWidth, 1);
            _amount.ScrollableModel.ViewSize = _amount.ViewSize;
            _from.Position = new Position(1 + boxWidth + 1, 1);
            _from.ViewSize = new Size(boxWidth, 1);
            _to.Position = new Position(1 + (boxWidth + 1) * 2, 1);
            _to.ViewSize = new Size(Math.Max(10, iw - (boxWidth + 1) * 2), 1);
        }

        base.OnInitializeLayout();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("F5", UiText.Get("DialogHint.RefreshRates", "Refresh the rates"));
        yield return ("F6", UiText.Get("DialogHint.SwapCurrencies", "Swap the two currencies"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift && !_from.IsOpen && !_to.IsOpen)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.F5:
                    StartRefresh();
                    return true;
                case ConsoleKey.F6:
                    SwapCurrencies();
                    return true;
            }
        }

        bool handled = await base.HandleKeyEventAsync(keyEvent);
        StateHasChanged();
        return handled;
    }

    #region Actions

    /// <summary>F5: downloads the rates on a background task; the old ones stay in use until the new ones land.</summary>
    internal void StartRefresh()
    {
        // A download still running is replaced: cancelled, and its outcome dropped by the new generation.
        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;
        int generation = ++_generation;
        _busy = true;
        SetStatus("Downloading the rates…", isError: false);

        RequestTask = Task.Run(async () =>
        {
            try
            {
                _pending = new Outcome(generation, await _service.RefreshAsync(token), null);
            }
            catch (OperationCanceledException)
            {
                _pending = new Outcome(generation, null, null);
            }
            catch (Exception exception)
            {
                _pending = new Outcome(generation, null, exception.Message);
            }
        });
    }

    /// <summary>F6: swaps the two currencies.</summary>
    internal void SwapCurrencies()
    {
        int from = _from.SelectedIndex;
        _from.SelectedIndex = _to.SelectedIndex;
        _to.SelectedIndex = from;
        StateHasChanged();
    }

    public override void OnIdle()
    {
        base.OnIdle();

        Outcome? outcome = Interlocked.Exchange(ref _pending, null);
        if (outcome is null || outcome.Generation != _generation)
        {
            return;
        }

        _busy = false;
        if (outcome.Rates is { } rates)
        {
            PopulateCurrencies(_from.SelectedItem?.Code ?? _settings.Get(FromKey) ?? "EUR", _to.SelectedItem?.Code ?? _settings.Get(ToKey) ?? "USD");
            SetStatus($"Rates of {rates.Date:yyyy-MM-dd} downloaded.", isError: false);
        }
        else if (outcome.Error is not null)
        {
            string kept = _service.Current is { } old ? $" Using the rates of {old.Date:yyyy-MM-dd}." : string.Empty;
            SetStatus(outcome.Error + kept, isError: true);
        }
        else
        {
            SetStatus("Cancelled.", isError: false);
        }
    }

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
        StateHasChanged();
    }

    #endregion

    #region Presentation

    /// <summary>Reads what was typed as an amount: a point or a comma for the decimals, spaces for the thousands.</summary>
    internal static bool TryParseAmount(string text, out decimal amount)
    {
        string cleaned = text.Trim().Replace(" ", string.Empty);
        if (cleaned.Contains(',') && !cleaned.Contains('.'))
        {
            cleaned = cleaned.Replace(',', '.');
        }
        else
        {
            cleaned = cleaned.Replace(",", string.Empty);
        }

        return decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount);
    }

    internal static string Format(decimal value) => value.ToString("#,##0.00####", CultureInfo.InvariantCulture);

    /// <summary>"100 EUR = 108.23 USD", or the reason there is no result yet.</summary>
    internal string ResultLine()
    {
        if (_service.Current is not { } rates)
        {
            return _busy ? "Waiting for the rates…" : "No rates yet — press F5 to download them.";
        }

        if (_from.SelectedItem is not { } from || _to.SelectedItem is not { } to)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(_amount.Text))
        {
            return $"Type an amount in {from.Code} to convert it to {to.Code}.";
        }

        if (!TryParseAmount(_amount.Text, out decimal amount))
        {
            return $"\"{_amount.Text}\" is not a number.";
        }

        decimal? converted = rates.Convert(amount, from.Code, to.Code);
        return converted is null
            ? $"No rate for {from.Code} → {to.Code}."
            : $"{Format(amount)} {from.Code}  =  {Format(converted.Value)} {to.Code}";
    }

    /// <summary>"1 EUR = 1.0823 USD · 1 USD = 0.9239 EUR", the rate both ways.</summary>
    internal string RateLine()
    {
        if (_service.Current is not { } rates || _from.SelectedItem is not { } from || _to.SelectedItem is not { } to
            || rates.RateBetween(from.Code, to.Code) is not { } forward || rates.RateBetween(to.Code, from.Code) is not { } back)
        {
            return string.Empty;
        }

        return $"1 {from.Code} = {Format(forward)} {to.Code}   ·   1 {to.Code} = {Format(back)} {from.Code}";
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int ix = (int)AbsolutePosition.X + 1;
        int iy = (int)AbsolutePosition.Y + 1;
        int iw = (int)ViewSize.Width - 2;
        int ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 0)
        {
            return;
        }

        string result = ResultLine();
        bool failed = result.Length == 0 || result.Contains("not a number", StringComparison.Ordinal) || result.StartsWith("No ", StringComparison.Ordinal);
        WriteLine(buffer, ix, iy + 2, iw, string.Empty, colorScheme.Foreground, colorScheme.Background);
        WriteLine(buffer, ix, iy + 3, iw, " " + result, failed ? colorScheme.Error : colorScheme.SelectedFocusedForeground, colorScheme.Background);
        WriteLine(buffer, ix, iy + 4, iw, " " + RateLine(), colorScheme.Foreground, colorScheme.Background);
        for (int row = 5; row < ih - 1; row++)
        {
            WriteLine(buffer, ix, iy + row, iw, string.Empty, colorScheme.Foreground, colorScheme.Background);
        }

        DrawStatusAndHints(buffer, ix, iy + ih - 1, iw, _status, _statusIsError ? colorScheme.Error : Color.Cyan, KeyHints, colorScheme);
    }

    #endregion
}

using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Portfolio;

namespace iTerminal.Core.Dialogs.Portfolio;

/// <summary>
/// First-time portfolio setup wizard: confirms the quote source and default lookup settings, then
/// lets the user pick from a set of default stocks (plus any custom symbols) to seed the portfolio
/// with. On completion it marks <see cref="ConfiguredSettingKey"/> and invokes
/// <see cref="OnCompleted"/> so the caller can open <see cref="PortfolioManagerDialog"/> next.
/// All I/O runs on background tasks; the key handler and <see cref="Draw"/> never block.
/// </summary>
public class PortfolioSetupWizardDialog : SplitPaneDialog
{
    public const string ConfiguredSettingKey = "PORTFOLIO_CONFIGURED";

    // Ctrl+&lt;letter&gt; hotkeys work regardless of which control currently has focus (they broadcast
    // to every enabled control), so stock selection and step navigation stay usable even though
    // Dialog.OnInitializeLayout() re-steals Tab focus to the first focusable control on every
    // terminal resize event. Letters are chosen to avoid colliding with each other or with the
    // Next/Finish/Cancel button hotkeys below.
    private static readonly (string Symbol, string Name, char HotKey)[] DefaultSymbols =
    [
        ("AAPL", "Apple Inc.", 'A'),
        ("MSFT", "Microsoft Corp.", 'M'),
        ("GOOGL", "Alphabet Inc.", 'G'),
        ("AMZN", "Amazon.com Inc.", 'Z'),
        ("TSLA", "Tesla Inc.", 'T'),
        ("NVDA", "NVIDIA Corp.", 'V'),
        ("META", "Meta Platforms Inc.", 'E'),
        ("JPM", "JPMorgan Chase & Co.", 'J'),
    ];

    private enum WizardStep
    {
        QuoteSource,
        Stocks,
    }

    private sealed class WizardActionCommand(Action action) : Command
    {
        public override Task ExecuteAsync(object? args)
        {
            action();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Plain <see cref="CheckBox"/> only focuses itself on its hotkey (the base
    /// <c>Control.TriggerHotKeyAsync</c> default) — unlike <see cref="Button"/>, it doesn't override
    /// that to actually act. This toggles <see cref="CheckBox.IsChecked"/> too, so Ctrl+&lt;letter&gt;
    /// reliably selects a stock regardless of which control currently has focus.
    /// </summary>
    private sealed class HotKeyCheckBox(IThemeService themeService) : CheckBox(themeService)
    {
        public override async Task TriggerHotKeyAsync()
        {
            IsChecked = !IsChecked;
            await base.TriggerHotKeyAsync();
        }
    }

    private readonly IPortfolioService _portfolioService;
    private readonly ISettingsService _settingsService;

    private readonly TextBox _exchangeTextBox;
    private readonly TextBox _currencyTextBox;
    private readonly Button _nextButton;
    private readonly List<(string Symbol, CheckBox Box)> _stockCheckBoxes = [];
    private readonly TextBox _customSymbolTextBox;
    private readonly Button _addCustomSymbolButton;
    private readonly Button _finishButton;
    private readonly Button _cancelButton;

    private readonly HashSet<string> _customSymbols = new(StringComparer.OrdinalIgnoreCase);

    private WizardStep _step = WizardStep.QuoteSource;
    private string _defaultExchange = string.Empty;
    private string _message = string.Empty;
    private bool _finishing;

    /// <summary>Invoked after setup completes and this dialog has closed itself.</summary>
    public Func<Task>? OnCompleted { get; set; }

    public PortfolioSetupWizardDialog(
        IPortfolioService portfolioService,
        ISettingsService settingsService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(PortfolioSetupWizardDialog), themeService)
    {
        _portfolioService = portfolioService;
        _settingsService = settingsService;
        Title = "Portfolio Setup";

        _exchangeTextBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = "Default exchange suffix (optional, e.g. US)",
        };
        _currencyTextBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = "Default currency",
            Text = "USD",
        };
        _nextButton = new Button("Next", new WizardActionCommand(SubmitQuoteSourceStep), themeService)
        {
            HotKeyChar = 'N',
        };
        AddControl(_exchangeTextBox);
        AddControl(_currencyTextBox);
        AddControl(_nextButton);

        foreach (var (symbol, name, hotKey) in DefaultSymbols)
        {
            var checkBox = new HotKeyCheckBox(themeService)
            {
                Label = $"{symbol,-6} {name} (Ctrl+{hotKey})",
                HotKeyChar = hotKey,
                IsVisible = false,
                IsEnabled = false,
                CanFocus = false,
            };
            AddControl(checkBox);
            _stockCheckBoxes.Add((symbol, checkBox));
        }

        _customSymbolTextBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = "Custom symbol (e.g. IBM)",
            IsVisible = false,
            IsEnabled = false,
            CanFocus = false,
        };
        _addCustomSymbolButton = new Button("Add symbol", new WizardActionCommand(AddCustomSymbol), themeService)
        {
            HotKeyChar = 'D',
            IsVisible = false,
            CanFocus = false,
        };
        _finishButton = new Button("Finish", new WizardActionCommand(FinishSetup), themeService)
        {
            HotKeyChar = 'F',
            IsVisible = false,
            CanFocus = false,
        };
        AddControl(_customSymbolTextBox);
        AddControl(_addCustomSymbolButton);
        AddControl(_finishButton);

        _cancelButton = new CancelDialogButton(themeService);
        AddControl(_cancelButton);
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.8);
        }

        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        if (iw > 0 && ih > 0)
        {
            var fieldWidth = Math.Max(1, iw - 4);
            _exchangeTextBox.Position = new Position(2, 4);
            _exchangeTextBox.ViewSize = new Size(fieldWidth, 1);
            _exchangeTextBox.ScrollableModel.ViewSize = _exchangeTextBox.ViewSize;
            _currencyTextBox.Position = new Position(2, 6);
            _currencyTextBox.ViewSize = new Size(fieldWidth, 1);
            _currencyTextBox.ScrollableModel.ViewSize = _currencyTextBox.ViewSize;

            var y = 4;
            foreach (var (_, checkBox) in _stockCheckBoxes)
            {
                checkBox.Position = new Position(2, y);
                checkBox.ViewSize = new Size(fieldWidth, 1);
                y++;
            }

            _customSymbolTextBox.Position = new Position(2, y + 1);
            _customSymbolTextBox.ViewSize = new Size(Math.Max(1, fieldWidth - 14), 1);
            _customSymbolTextBox.ScrollableModel.ViewSize = _customSymbolTextBox.ViewSize;
            _addCustomSymbolButton.Position = new Position(iw - 12, y + 1);
            _addCustomSymbolButton.ViewSize = new Size(12, 1);

            _nextButton.Position = new Position(2, ih - 2);
            _nextButton.ViewSize = new Size(12, 1);
            _finishButton.Position = new Position(2, ih - 2);
            _finishButton.ViewSize = new Size(12, 1);
            _cancelButton.Position = new Position(16, ih - 2);
            _cancelButton.ViewSize = new Size(12, 1);
        }

        // Children must have their Position/ViewSize assigned before base.OnInitializeLayout()
        // recurses into them, same reason PortfolioManagerDialog orders it this way.
        base.OnInitializeLayout();
    }

    /// <summary>
    /// Shows the controls of the current step and hides the other step's. <c>Control.ShowAsync</c>
    /// makes every child visible on the way in, so this has to run after it too: the wizard used to
    /// open with the eight stock boxes, the custom-symbol box and the Finish button all drawn over
    /// step 1's fields — Finish on top of Next.
    /// </summary>
    private void ApplyStepVisibility()
    {
        var first = _step == WizardStep.QuoteSource;

        foreach (var control in new iConsole.Controls.Control[] { _exchangeTextBox, _currencyTextBox, _nextButton })
        {
            control.IsVisible = first;
            control.CanFocus = first;
        }

        foreach (var (_, checkBox) in _stockCheckBoxes)
        {
            checkBox.IsVisible = !first;
            checkBox.IsEnabled = !first;
            checkBox.CanFocus = !first;
        }

        _customSymbolTextBox.IsVisible = !first;
        _customSymbolTextBox.IsEnabled = !first;
        _customSymbolTextBox.CanFocus = !first;
        _addCustomSymbolButton.IsVisible = !first;
        _addCustomSymbolButton.CanFocus = !first;
        _finishButton.IsVisible = !first;
        _finishButton.CanFocus = !first;
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        ApplyStepVisibility();
    }

    /// <summary>
    /// Enter does what a form's Enter does: on step 1 it moves on, in the custom-symbol box it adds
    /// the symbol. Both used to need their Ctrl chord (or a trip to the button with Tab), and a
    /// symbol typed and "entered" was silently left in the box. On a stock box Enter is left to the
    /// box, and on a button to the button.
    /// </summary>
    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Control && !keyEvent.Alt)
        {
            if (_step == WizardStep.QuoteSource && (_exchangeTextBox.IsFocused || _currencyTextBox.IsFocused))
            {
                SubmitQuoteSourceStep();
                return true;
            }

            if (_step == WizardStep.Stocks && _customSymbolTextBox.IsFocused)
            {
                AddCustomSymbol();
                return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    internal bool IsOnStocksStepForTest => _step == WizardStep.Stocks;
    internal bool AreStockBoxesVisibleForTest => _stockCheckBoxes.All(c => c.Box.IsVisible);
    internal bool IsNextVisibleForTest => _nextButton.IsVisible;
    internal bool IsFinishVisibleForTest => _finishButton.IsVisible;
    internal TextBox CustomSymbolBoxForTest => _customSymbolTextBox;
    internal TextBox ExchangeBoxForTest => _exchangeTextBox;
    internal string MessageForTest => _message;

    #region Step 1: quote source

    private void SubmitQuoteSourceStep()
    {
        _defaultExchange = _exchangeTextBox.Text.Trim().ToUpperInvariant();
        var currency = _currencyTextBox.Text.Trim().ToUpperInvariant();
        _currencyTextBox.Text = currency.Length == 0 ? "USD" : currency;
        BeginStocksStep();
    }

    #endregion

    #region Step 2: stocks

    private void BeginStocksStep()
    {
        _step = WizardStep.Stocks;
        ApplyStepVisibility();

        _message = string.Empty;
        if (_stockCheckBoxes.Count > 0)
        {
            _stockCheckBoxes[0].Box.SetFocus();
        }

        StateHasChanged();
    }

    private void AddCustomSymbol()
    {
        var symbol = _customSymbolTextBox.Text.Trim().ToUpperInvariant();
        if (symbol.Length == 0)
        {
            return;
        }

        _customSymbols.Add(symbol);
        _customSymbolTextBox.Text = string.Empty;
        _message = $"Added {symbol}. Press Finish when you're done, or add another.";
        StateHasChanged();
    }

    private void FinishSetup()
    {
        if (_finishing)
        {
            return;
        }

        _finishing = true;
        _message = "Setting up portfolio...";
        StateHasChanged();

        var symbols = _stockCheckBoxes
            .Where(c => c.Box.IsChecked)
            .Select(c => c.Symbol)
            .Concat(_customSymbols)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var exchange = _defaultExchange.Length == 0 ? null : _defaultExchange;
        var currency = _currencyTextBox.Text;

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var symbol in symbols)
                {
                    await _portfolioService.AddHoldingAsync(symbol, exchange, quantity: 0m, costBasisPerUnit: 0m, currency, acquiredDate: null, notes: null);
                }

                _settingsService.Set(ConfiguredSettingKey, "true");
                _settingsService.Write();

                Close();

                if (OnCompleted is not null)
                {
                    await OnCompleted();
                }
            }
            catch (Exception exception)
            {
                _finishing = false;
                _message = $"Error: {exception.Message}";
                StateHasChanged();
            }
        });
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

        DrawHeader(buffer, ix, iy, iw);

        if (_step == WizardStep.QuoteSource)
        {
            WriteLine(buffer, ix, iy + 1, iw, "Quote source: Stooq (free, delayed quotes, no API key required).", colorScheme.Foreground, colorScheme.Background);
        }
        else
        {
            DrawSectionHeader(buffer, ix, iy + 1, iw, "Select stocks to add", true, colorScheme);
        }

        DrawFooter(buffer, ix, iy + ih - 1, iw, colorScheme);
    }

    private void DrawHeader(IBuffer buffer, int x, int y, int w)
    {
        var stepText = _step == WizardStep.QuoteSource ? "Step 1 of 2: Quote Source" : "Step 2 of 2: Select Stocks";
        WriteLine(buffer, x, y, w, $"Portfolio Setup — {stepText}", Color.Cyan, ThemeService.CurrentColorScheme.Background);
    }

    private void DrawFooter(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        var fg = colorScheme.Foreground;
        var text = _message;
        if (text.Length == 0)
        {
            text = _step == WizardStep.QuoteSource
                ? "Tab:move  Enter or Ctrl+N:next  Esc:cancel"
                : "Ctrl+letter or Space:toggle a stock  Enter:add typed symbol  Ctrl+F:finish  Esc:cancel";
        }

        if (text.StartsWith("Error", StringComparison.Ordinal))
        {
            fg = colorScheme.Error;
        }

        WriteLine(buffer, x, y, w, text, fg, colorScheme.Background);
    }

    #endregion
}

using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Jobs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Portfolio;
using System.Globalization;

namespace iTerminal.Core.Dialogs.Portfolio;

/// <summary>
/// Full-screen modal portfolio tracker: a list of holdings (symbol, quantity, cost basis) joined
/// against live quotes from <see cref="OnlineQuoteService.Instruments"/> (kept warm in the
/// background by <see cref="PortfolioQuoteJob"/>), showing present market value and win/loss per
/// holding plus aggregate totals. All I/O runs on background tasks; the key handler and
/// <see cref="Draw"/> never block.
/// </summary>
public class PortfolioManagerDialog : SplitPaneDialog
{
    internal sealed record PortfolioHoldingRow(PortfolioHolding Holding, decimal? CurrentPrice, DateTime? QuoteTimestamp)
    {
        internal decimal CostBasisTotal => Holding.Quantity * Holding.CostBasisPerUnit;
        internal decimal? MarketValue => CurrentPrice is decimal price ? Holding.Quantity * price : null;
        internal decimal? GainLossAbsolute => MarketValue is decimal value ? value - CostBasisTotal : null;
        internal decimal? GainLossPercent => GainLossAbsolute is decimal gainLoss && CostBasisTotal != 0 ? gainLoss / CostBasisTotal * 100m : null;
    }

    private sealed class HoldingListBox : ListBox<PortfolioHoldingRow>
    {
        public HoldingListBox(IListBoxModel<PortfolioHoldingRow> listModel, IThemeService themeService)
            : base(listModel, themeService)
        {
            ListModel.TransformItem = TransformRow;
        }

        private ListItem<PortfolioHoldingRow> TransformRow(PortfolioHoldingRow row, CancellationToken cancellationToken)
        {
            var holding = row.Holding;
            var price = row.CurrentPrice?.ToString("N2", CultureInfo.InvariantCulture) ?? "--";
            var value = row.MarketValue?.ToString("N2", CultureInfo.InvariantCulture) ?? "--";
            var gainLoss = row.GainLossAbsolute?.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) ?? "--";
            var gainLossPercent = row.GainLossPercent?.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) ?? "--";
            var text = " " + FormatColumns(holding.Symbol, holding.Quantity.ToString("N2", CultureInfo.InvariantCulture),
                holding.CostBasisPerUnit.ToString("N2", CultureInfo.InvariantCulture), price, value, gainLoss, gainLossPercent + "%");
            return new ListItem<PortfolioHoldingRow>(row) { Value = text.Shrink(Math.Max(1, (int)ViewSize.Width)) ?? string.Empty, IsEnabled = true };
        }

        protected override Color? GetItemForegroundOverride(int index)
        {
            if (index < 0 || index >= ListModel.ListItems.Count)
            {
                return null;
            }

            return ListModel.ListItems[index].Item.GainLossAbsolute switch
            {
                > 0 => Color.Green,
                < 0 => Color.Red,
                _ => null,
            };
        }
    }

    /// <summary>
    /// One layout for the rows and for the column headings above them. The rows used to be bare
    /// numbers under a "Holdings" band, with nothing to say which column was the price and which
    /// the gain.
    /// </summary>
    internal static string FormatColumns(string symbol, string quantity, string cost, string price, string value, string gainLoss, string gainLossPercent) =>
        $"{symbol,-8} {quantity,10} {cost,10} {price,10} {value,12} {gainLoss,12} {gainLossPercent,9}";

    internal static readonly string ColumnHeadings =
        FormatColumns("Symbol", "Quantity", "Cost/unit", "Price", "Value", "Gain/loss", "G/L %");

    /// <summary>
    /// Reads a number the way it was typed. It used to be read invariantly with thousands
    /// separators allowed, so "1,5" typed with a decimal comma was stored as 15. A comma or a point
    /// as the decimal separator both work now; thousands separators are not accepted, since
    /// "1,500" is exactly the ambiguous case.
    /// </summary>
    internal static bool TryParseAmount(string text, out decimal value)
    {
        const NumberStyles styles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite
            | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        text = (text ?? string.Empty).Trim();
        return decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out value)
            || decimal.TryParse(text.Replace(',', '.'), styles, CultureInfo.InvariantCulture, out value);
    }

    private readonly IPortfolioService _portfolioService;
    private readonly OnlineQuoteService _onlineQuoteService;
    private readonly IQuoteProvider _quoteProvider;

    private readonly ConcurrentObservableCollection<PortfolioHoldingRow> _rowItems = new();
    private readonly HoldingListBox _holdingsListBox;
    private readonly TextBox _inputBox;

    private List<PortfolioHolding> _holdings = [];
    private bool _loading;
    private string _message = string.Empty;

    private Action<string>? _onInputSubmit;
    private string _confirmPrompt = string.Empty;
    private Action? _confirmAction;

    public PortfolioManagerDialog(
        IPortfolioService portfolioService,
        OnlineQuoteService onlineQuoteService,
        IQuoteProvider quoteProvider,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(PortfolioManagerDialog), themeService)
    {
        _portfolioService = portfolioService;
        _onlineQuoteService = onlineQuoteService;
        _quoteProvider = quoteProvider;
        Title = "Portfolio";

        _holdingsListBox = new HoldingListBox(new ListBoxModel<PortfolioHoldingRow>(_rowItems), themeService)
        {
            EmptyText = " No holdings yet — press a to add one.",
        };
        _inputBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            IsVisible = false,
            IsEnabled = false,
            CanFocus = false,
        };

        AddControl(_holdingsListBox);
        AddControl(_inputBox);
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        StartRefresh();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.95);
        }

        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        if (iw > 0 && ih > 0)
        {
            var listH = Math.Max(0, ih - 4);
            _holdingsListBox.Position = new Position(1, 3);
            _holdingsListBox.ViewSize = new Size(iw, listH);

            _inputBox.Position = new Position(1, ih);
            _inputBox.ViewSize = new Size(iw, 1);
            _inputBox.ScrollableModel.ViewSize = _inputBox.ViewSize;
        }

        // Children must have their Position/ViewSize assigned before base.OnInitializeLayout()
        // recurses into them, same reason GitClientDialog orders it this way.
        base.OnInitializeLayout();
    }

    #region Refresh (background)

    private void StartRefresh()
    {
        _loading = true;
        _message = string.Empty;
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            try
            {
                _holdings = await _portfolioService.GetHoldingsAsync();
                RecomputeRows();
                _loading = false;
                StateHasChanged();
            }
            catch (Exception exception)
            {
                _message = $"Error: {exception.Message}";
                _loading = false;
                StateHasChanged();
            }
        });
    }

    /// <summary>Pure in-memory join of the loaded holdings against the shared quote dictionary — no I/O.</summary>
    private void RecomputeRows()
    {
        var rows = _holdings
            .Select(holding =>
            {
                var symbol = PortfolioQuoteJob.BuildStooqSymbol(holding);
                var instrumentValue = _onlineQuoteService.Instruments.TryGetValue(symbol, out var instrument)
                    ? instrument.InstrumentValues.FirstOrDefault()
                    : null;
                return new PortfolioHoldingRow(holding, instrumentValue?.Value, instrumentValue?.ValueDateTime);
            })
            .ToList();

        _rowItems.ReplaceAll(rows);
        _holdingsListBox.UpdateModel();
        ClampCursor(_holdingsListBox.ScrollableModel, rows.Count);
        StateHasChanged();
    }

    private static void ClampCursor(IScrollableModel scrollableModel, int count)
    {
        scrollableModel.CursorPosition.Y = count <= 0 ? 0 : Math.Clamp(scrollableModel.CursorPosition.Y, 0, count - 1);
        scrollableModel.ScrollPosition.Y = count <= 0 ? 0 : Math.Clamp(scrollableModel.ScrollPosition.Y, 0, count - 1);
    }

    /// <summary>
    /// Fetches quotes right now instead of waiting for <see cref="PortfolioQuoteJob"/>'s own tick,
    /// so 'r' gives immediate feedback. Writes into the same shared <see cref="OnlineQuoteService"/>
    /// dictionary the background job uses, so both stay consistent.
    /// </summary>
    private void ManualRefreshQuotes()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        _message = "Refreshing quotes...";
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            try
            {
                var holdingsBySymbol = _holdings
                    .GroupBy(PortfolioQuoteJob.BuildStooqSymbol)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                if (holdingsBySymbol.Count > 0)
                {
                    var quotes = await _quoteProvider.GetQuotesAsync(holdingsBySymbol.Keys.ToList());
                    foreach (var (symbol, quote) in quotes)
                    {
                        if (!holdingsBySymbol.TryGetValue(symbol, out var holding))
                        {
                            continue;
                        }

                        if (!_onlineQuoteService.Instruments.TryGetValue(symbol, out var instrument))
                        {
                            instrument = new Instrument
                            {
                                Id = Guid.NewGuid(),
                                Currency = holding.Currency,
                                Name = symbol,
                                InstrumentType = InstrumentType.Stock
                            };
                            _onlineQuoteService.Instruments[symbol] = instrument;
                        }

                        instrument.InstrumentValues.InsertAt(0, new InstrumentValue
                        {
                            Id = Guid.NewGuid(),
                            InstrumentId = instrument.Id,
                            Source = nameof(PortfolioManagerDialog),
                            SourceDateTime = quote.Timestamp,
                            Value = quote.Close,
                            ValueDateTime = quote.Timestamp,
                            ValueType = InstrumentValueType.Intraday
                        });
                    }
                }

                _message = string.Empty;
                _loading = false;
                RecomputeRows();
            }
            catch (Exception exception)
            {
                _message = $"Error: {exception.Message}";
                _loading = false;
                StateHasChanged();
            }
        });
    }

    #endregion

    #region Mutating actions (background)

    private void BeginAddHolding()
    {
        ShowInput("Symbol (1/5)", symbolText =>
        {
            var symbol = symbolText.Trim().ToUpperInvariant();
            if (symbol.Length == 0)
            {
                _message = "Symbol is required.";
                StateHasChanged();
                return;
            }

            ShowInput("Exchange, optional, e.g. US (2/5)", exchangeText =>
            {
                var exchange = exchangeText.Trim().ToUpperInvariant();

                ShowInput("Quantity (3/5)", quantityText =>
                {
                    if (!TryParseAmount(quantityText, out var quantity))
                    {
                        _message = "Error: invalid quantity.";
                        StateHasChanged();
                        return;
                    }

                    ShowInput("Cost per unit (4/5)", costText =>
                    {
                        if (!TryParseAmount(costText, out var cost))
                        {
                            _message = "Error: invalid cost per unit.";
                            StateHasChanged();
                            return;
                        }

                        ShowInput("Currency (5/5)", currencyText =>
                        {
                            var currency = currencyText.Trim().ToUpperInvariant();
                            if (currency.Length == 0)
                            {
                                _message = "Currency is required.";
                                StateHasChanged();
                                return;
                            }

                            DoAdd(symbol, exchange.Length == 0 ? null : exchange, quantity, cost, currency);
                        });
                    });
                });
            });
        });
    }

    private void DoAdd(string symbol, string? exchange, decimal quantity, decimal costBasisPerUnit, string currency)
    {
        _message = $"Adding {symbol}...";
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            try
            {
                await _portfolioService.AddHoldingAsync(symbol, exchange, quantity, costBasisPerUnit, currency, acquiredDate: null, notes: null);
                _holdings = await _portfolioService.GetHoldingsAsync();
                _message = string.Empty;
                RecomputeRows();
            }
            catch (Exception exception)
            {
                _message = $"Error: {exception.Message}";
                StateHasChanged();
            }
        });
    }

    private void BeginEditHolding()
    {
        var row = CursorItem(_holdingsListBox);
        if (row is null)
        {
            return;
        }

        var holding = row.Holding;
        ShowInput("Quantity", quantityText =>
        {
            if (!TryParseAmount(quantityText, out var quantity))
            {
                _message = "Error: invalid quantity.";
                StateHasChanged();
                return;
            }

            ShowInput("Cost per unit", costText =>
            {
                if (!TryParseAmount(costText, out var cost))
                {
                    _message = "Error: invalid cost per unit.";
                    StateHasChanged();
                    return;
                }

                DoEdit(holding, quantity, cost);
            }, prefill: holding.CostBasisPerUnit.ToString(CultureInfo.InvariantCulture));
        }, prefill: holding.Quantity.ToString(CultureInfo.InvariantCulture));
    }

    private void DoEdit(PortfolioHolding holding, decimal quantity, decimal costBasisPerUnit)
    {
        _message = $"Updating {holding.Symbol}...";
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            try
            {
                holding.Quantity = quantity;
                holding.CostBasisPerUnit = costBasisPerUnit;
                await _portfolioService.UpdateHoldingAsync(holding);
                _holdings = await _portfolioService.GetHoldingsAsync();
                _message = string.Empty;
                RecomputeRows();
            }
            catch (Exception exception)
            {
                // The holding was changed in place before the save; put the list back to what is
                // stored so the screen does not show an edit that failed.
                _message = $"Error: {exception.Message}";
                try
                {
                    _holdings = await _portfolioService.GetHoldingsAsync();
                    RecomputeRows();
                }
                catch
                {
                    StateHasChanged();
                }
            }
        });
    }

    private void BeginDeleteHolding()
    {
        var row = CursorItem(_holdingsListBox);
        if (row is null)
        {
            return;
        }

        var holding = row.Holding;
        BeginConfirm($"Delete {holding.Symbol}?  y:yes  any other key:no", () =>
        {
            _message = $"Deleting {holding.Symbol}...";
            StateHasChanged();

            _ = Task.Run(async () =>
            {
                try
                {
                    await _portfolioService.DeleteHoldingAsync(holding.Id);
                    _holdings = await _portfolioService.GetHoldingsAsync();
                    _message = string.Empty;
                    RecomputeRows();
                }
                catch (Exception exception)
                {
                    _message = $"Error: {exception.Message}";
                    StateHasChanged();
                }
            });
        });
    }

    #endregion

    #region Input

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_confirmAction is not null)
        {
            return HandleConfirmKey(keyEvent);
        }

        if (_inputActive)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.Enter:
                    SubmitInput();
                    return true;
                case ConsoleKey.Escape:
                    CancelInput();
                    return true;
            }

            return await base.HandleKeyEventAsync(keyEvent);
        }

        switch (char.ToLowerInvariant(keyEvent.KeyChar))
        {
            case 'a':
                BeginAddHolding();
                return true;
            case 'e':
                BeginEditHolding();
                return true;
            case 'd':
                BeginDeleteHolding();
                return true;
            case 'r':
                ManualRefreshQuotes();
                return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>
    /// Whether the inline input is up. Tracked here rather than read from the box's
    /// <c>IsVisible</c>, because <c>Control.ShowAsync</c> makes every child visible on the way in:
    /// read that way the box was "up" from the moment the dialog opened, and the footer — the key
    /// hints and the y/n prompt of a delete — was never drawn.
    /// </summary>
    private bool _inputActive;

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        ApplyInputVisibility();
    }

    private void ApplyInputVisibility()
    {
        _inputBox.CanFocus = _inputActive;
        _inputBox.IsVisible = _inputActive;
        _inputBox.IsEnabled = _inputActive;
    }

    internal bool IsInputActiveForTest => _inputActive;
    internal bool IsInputBoxVisibleForTest => _inputBox.IsVisible;

    private void ShowInput(string label, Action<string> onSubmit, string prefill = "")
    {
        _onInputSubmit = onSubmit;
        _inputActive = true;
        _inputBox.Label = label;
        _inputBox.Text = prefill;
        ApplyInputVisibility();
        _inputBox.SetFocus();
        StateHasChanged();
    }

    private void SubmitInput()
    {
        var text = _inputBox.Text;
        var callback = _onInputSubmit;
        HideInput();
        callback?.Invoke(text);
    }

    private void CancelInput() => HideInput();

    private void HideInput()
    {
        _inputBox.ResetFocus();
        _inputActive = false;
        ApplyInputVisibility();
        _inputBox.Text = string.Empty;
        _onInputSubmit = null;
        _holdingsListBox.SetFocus();
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
            _message = string.Empty;
            StateHasChanged();
        }

        return true;
    }

    private void BeginConfirm(string prompt, Action onConfirm)
    {
        _confirmPrompt = prompt;
        _confirmAction = onConfirm;
        StateHasChanged();
    }

    private static PortfolioHoldingRow? CursorItem(HoldingListBox box)
    {
        var items = box.ListModel.ListItems;
        var index = (int)box.ScrollableModel.CursorPosition.Y;
        return index >= 0 && index < items.Count ? items[index].Item : null;
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

        DrawHeader(buffer, ix, iy, iw, colorScheme);
        DrawSectionHeader(buffer, ix, iy + 1, iw, ColumnHeadings, _holdingsListBox.IsFocused, colorScheme);

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
        var count = _rowItems.Count;
        var loading = _loading ? "  (loading…)" : string.Empty;
        WriteLine(buffer, x, y, w, $"Portfolio — {count} holding{(count == 1 ? string.Empty : "s")}{loading}", Color.Cyan, colorScheme.Background);
    }

    private void DrawSummary(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        var rows = _rowItems.ToList();
        var totalCost = rows.Sum(r => r.CostBasisTotal);
        var quotedRows = rows.Where(r => r.CurrentPrice.HasValue).ToList();
        var quotedCost = quotedRows.Sum(r => r.CostBasisTotal);
        var totalValue = quotedRows.Sum(r => r.MarketValue!.Value);
        var totalGainLoss = totalValue - quotedCost;
        var totalGainLossPercent = quotedCost != 0 ? totalGainLoss / quotedCost * 100m : (decimal?)null;

        // Cost and value over the same holdings: the total cost of everything set against the value
        // of only the quoted part read as a loss the size of every unquoted holding.
        var unquoted = rows.Count - quotedRows.Count;
        var text = $"Cost: {quotedCost:N2}   Value: {totalValue:N2}   G/L: {totalGainLoss:+0.00;-0.00}" +
            (totalGainLossPercent is decimal percent ? $" ({percent:+0.00;-0.00}%)" : string.Empty) +
            (unquoted > 0 ? $"   {unquoted} not quoted yet (cost {totalCost - quotedCost:N2})" : string.Empty);

        var foreground = totalGainLoss switch
        {
            > 0 => Color.Green,
            < 0 => Color.Red,
            _ => colorScheme.Foreground,
        };

        WriteLine(buffer, x, y, w, text, foreground, colorScheme.Background);
    }

    internal const string KeyHints = "a:add  e:edit  d:delete  r:refresh quotes  Esc:close";

    private void DrawFooter(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        if (_confirmAction is not null)
        {
            WriteLine(buffer, x, y, w, _confirmPrompt, Color.Red, colorScheme.Background);
            return;
        }

        var fg = _message.StartsWith("Error", StringComparison.Ordinal) ? colorScheme.Error : colorScheme.Foreground;
        DrawStatusAndHints(buffer, x, y, w, _message, fg, KeyHints, colorScheme);
    }

    internal string MessageForTest => _message;
    internal bool IsLoadingForTest => _loading;
    internal bool IsConfirmingForTest => _confirmAction is not null;
    internal IReadOnlyList<PortfolioHoldingRow> RowsForTest => [.. _rowItems.AsSnapshot()];
    internal string InputLabelForTest => _inputBox.Label;
    internal TextBox InputBoxForTest => _inputBox;

    #endregion
}

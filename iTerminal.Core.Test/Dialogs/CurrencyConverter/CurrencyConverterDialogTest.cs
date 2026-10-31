using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Services;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs.CurrencyConverter;
using iTerminal.Core.Services.Currency;
using iTerminal.Core.Test.Services.Currency;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Dialogs.CurrencyConverter;

[TestClass]
public class CurrencyConverterDialogTest
{
    private static (CurrencyConverterDialog Dialog, CurrencyTest.FakeProvider Provider, CurrencyTest.MemorySettings Settings) Create(
        CurrencyTest.FakeProvider? provider = null, CurrencyTest.MemorySettings? settings = null)
    {
        provider ??= new CurrencyTest.FakeProvider();
        settings ??= new CurrencyTest.MemorySettings();
        var theme = TestTheme();
        var dialog = new CurrencyConverterDialog(
            new CurrencyService(provider, settings), settings, Mock.Of<IBeepService>(), new DateTimeProviderService(), theme);
        return (dialog, provider, settings);
    }

    private static async Task SettleAsync(CurrencyConverterDialog dialog)
    {
        await dialog.RequestTask;
        dialog.ApplyPendingForTest();
    }

    private static void DrawChildren(IControl parent, IBuffer buffer)
    {
        foreach (var control in parent.Controls.AsSnapshot().Where(c => c.IsVisible))
        {
            control.Draw(buffer);
            DrawChildren(control, buffer);
        }
    }

    private static string Drawn(CurrencyConverterDialog dialog, int width = 120, int height = 40)
    {
        var buffer = new ScreenBuffer(width, height);
        // Twice, so text left over from the first frame would show.
        for (int pass = 0; pass < 2; pass++)
        {
            dialog.Draw(buffer);
            DrawChildren(dialog, buffer);
        }

        return string.Join("\n", Enumerable.Range(0, height).Select(buffer.Line));
    }

    [TestMethod]
    public async Task ItDownloadsTheRatesOnOpeningAndConvertsOneEuroToDollars()
    {
        var (dialog, provider, _) = Create();
        await ShowAsync(dialog);
        await SettleAsync(dialog);

        Assert.AreEqual(1, provider.Calls);
        Assert.AreEqual("EUR", dialog.FromBox.SelectedItem!.Code);
        Assert.AreEqual("USD", dialog.ToBox.SelectedItem!.Code);
        string screen = Drawn(dialog);
        StringAssert.Contains(screen, "1.00 EUR  =  2.00 USD");
        StringAssert.Contains(screen, "1 EUR = 2.00 USD");
        StringAssert.Contains(screen, "Rates of 2026-10-16 downloaded.");
    }

    [TestMethod]
    public async Task FreshKeptRatesAreShownAtOnceAndStillRefreshed()
    {
        var settings = new CurrencyTest.MemorySettings();
        settings.Values[CurrencyService.RatesKey] = CurrencyTest.Sample().Serialize();
        var (dialog, provider, _) = Create(settings: settings);
        await ShowAsync(dialog);

        StringAssert.Contains(Drawn(dialog), "1.00 EUR  =  2.00 USD");
        await SettleAsync(dialog);
        Assert.AreEqual(1, provider.Calls);
    }

    [TestMethod]
    public async Task TheAmountAcceptsACommaAndSwapTurnsTheConversionAround()
    {
        var (dialog, _, _) = Create();
        await ShowAsync(dialog);
        await SettleAsync(dialog);

        dialog.AmountBox.Text = "10,5";
        StringAssert.Contains(Drawn(dialog), "10.50 EUR  =  21.00 USD");

        dialog.SwapCurrencies();
        StringAssert.Contains(Drawn(dialog), "10.50 USD  =  5.25 EUR");
    }

    [TestMethod]
    public async Task ANonNumberIsSaidToBeOne()
    {
        var (dialog, _, _) = Create();
        await ShowAsync(dialog);
        await SettleAsync(dialog);

        dialog.AmountBox.Text = "abc";

        StringAssert.Contains(Drawn(dialog), "\"abc\" is not a number.");
    }

    [TestMethod]
    public async Task AFailedDownloadSaysSoAndKeepsTheKeptRates()
    {
        var settings = new CurrencyTest.MemorySettings();
        settings.Values[CurrencyService.RatesKey] = CurrencyTest.Sample().Serialize();
        var provider = new CurrencyTest.FakeProvider(failure: new CurrencyException("Could not download the rates."));
        var (dialog, _, _) = Create(provider, settings);
        await ShowAsync(dialog);
        await SettleAsync(dialog);

        string screen = Drawn(dialog);
        StringAssert.Contains(screen, "Could not download the rates. Using the rates of 2026-10-16.");
        StringAssert.Contains(screen, "1.00 EUR  =  2.00 USD");
    }

    [TestMethod]
    public async Task WithNoRatesAtAllItSaysHowToGetThem()
    {
        var provider = new CurrencyTest.FakeProvider(failure: new CurrencyException("offline"));
        var (dialog, _, _) = Create(provider);
        await ShowAsync(dialog);
        await SettleAsync(dialog);

        StringAssert.Contains(Drawn(dialog), "No rates yet — press F5 to download them.");
    }

    [TestMethod]
    public async Task ADownloadCancelledLateDoesNotOverwriteTheNewerRates()
    {
        // The dialog downloads on its own when it opens. Once that has landed, the first download the test starts holds
        // until the second has finished, and only then reports its cancellation, so its outcome lands last.
        var provider = new CurrencyTest.FakeProvider();
        var (dialog, _, _) = Create(provider);
        await ShowAsync(dialog);
        await SettleAsync(dialog);

        TaskCompletionSource gate = new();
        provider.Gate = gate.Task;
        provider.IgnoresCancellation = true;
        dialog.StartRefresh();
        Task first = dialog.RequestTask;
        await WaitUntilAsync(() => provider.Calls == 2, "the first download to reach the provider");

        provider.Gate = null;
        dialog.StartRefresh();
        await dialog.RequestTask;
        gate.SetResult();
        try
        {
            await first;
        }
        catch (OperationCanceledException)
        {
        }

        await SettleAsync(dialog);

        Assert.IsFalse(dialog.IsBusy);
        StringAssert.Contains(dialog.Status, "downloaded");
        Assert.IsFalse(dialog.Status.Contains("Cancelled", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ClosingKeepsTheChoices()
    {
        var (dialog, _, settings) = Create();
        await ShowAsync(dialog);
        await SettleAsync(dialog);
        dialog.AmountBox.Text = "42";
        dialog.SwapCurrencies();

        dialog.Close();

        Assert.AreEqual("42", settings.Values[CurrencyConverterDialog.AmountKey]);
        Assert.AreEqual("USD", settings.Values[CurrencyConverterDialog.FromKey]);
        Assert.AreEqual("EUR", settings.Values[CurrencyConverterDialog.ToKey]);
    }

    [TestMethod]
    [DataRow("1234.5", 1234.5)]
    [DataRow("1 234,5", 1234.5)]
    [DataRow("1,234.5", 1234.5)]
    [DataRow("-3", -3.0)]
    public void AmountsAreReadWhicheverWayTheyAreWritten(string text, double expected)
    {
        Assert.IsTrue(CurrencyConverterDialog.TryParseAmount(text, out decimal amount));
        Assert.AreEqual((decimal)expected, amount);
    }
}

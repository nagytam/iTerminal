using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Display;
using iTerminal.Core.Controls.MenuBar;
using iTerminal.Core.Data;
using iTerminal.Core.Services;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Controls.MenuBar.Test;

[TestClass]
public class QuoteIndicatorTest
{
    private static Instrument Bitcoin(params decimal[] newestFirst)
    {
        var instrument = new Instrument { Id = Guid.NewGuid(), Name = "Bitcoin", Currency = "BTC/USD" };
        foreach (var value in newestFirst)
        {
            instrument.InstrumentValues.Add(new InstrumentValue
            {
                Id = Guid.NewGuid(),
                InstrumentId = instrument.Id,
                ValueDateTime = DateTime.Now,
                Value = value,
                ValueType = default,
                Source = "test",
                SourceDateTime = DateTime.Now,
            });
        }

        return instrument;
    }

    private static (QuoteIndicator Indicator, OnlineQuoteService Service) Create(Instrument? instrument = null)
    {
        var service = new OnlineQuoteService();
        if (instrument is not null)
        {
            service.Instruments[instrument.Name] = instrument;
        }

        return (new QuoteIndicator("Bitcoin", service, TestTheme()), service);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WhileLoadingItNamesTheInstrument()
    {
        // Two indicators side by side both used to read "Fetch quote".
        var (indicator, _) = Create();

        StringAssert.StartsWith(indicator.Describe(18).Text, "Bitcoin ");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ShowsTheGroupedPriceWithThePair()
    {
        var (indicator, _) = Create(Bitcoin(82953m));

        var (text, _) = indicator.Describe(30);

        StringAssert.StartsWith(text, $"BTC/USD {82953m:N2}");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARiseIsGreenWithAnUpArrow()
    {
        var (indicator, _) = Create(Bitcoin(101m, 100m));

        var (text, background) = indicator.Describe(30);

        Assert.AreEqual(Color.Green, background);
        StringAssert.Contains(text, $"{101m:N2}{Border.ArrowUp}");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFallIsRedWithADownArrow()
    {
        var (indicator, _) = Create(Bitcoin(99m, 100m));

        var (text, background) = indicator.Describe(30);

        Assert.AreEqual(Color.Red, background);
        StringAssert.Contains(text, $"{99m:N2}{Border.ArrowDown}");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnUnchangedPriceHasNoArrowOrBand()
    {
        var (indicator, _) = Create(Bitcoin(100m, 100m));

        var (text, background) = indicator.Describe(30);

        Assert.AreEqual(TestTheme().CurrentColorScheme.Background, background);
        Assert.IsFalse(text.Contains(Border.ArrowUp) || text.Contains(Border.ArrowDown));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheArrowSurvivesANarrowSlot()
    {
        // The arrow is written last and was the first thing the buffer clipped.
        var (indicator, _) = Create(Bitcoin(82953m, 80000m));

        var (text, _) = indicator.Describe(12);

        Assert.AreEqual(12, text.Length);
        Assert.IsTrue(text.TrimEnd().EndsWith(Border.ArrowUp), text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ACachedValueIsMarkedAndStillFitsTheMenuBarSlot()
    {
        var (indicator, service) = Create(Bitcoin(82953m));
        service.IsFromCache = true;

        var wide = indicator.Describe(40).Text;
        var slot = indicator.Describe(18).Text;

        StringAssert.StartsWith(wide, QuoteIndicator.CachedMarker);
        Assert.AreEqual(18, slot.Length);
        Assert.IsTrue(slot.StartsWith(QuoteIndicator.CachedMarker) || slot.StartsWith('*'), $"still marked as cached: '{slot}'");
        StringAssert.Contains(slot, $"{82953m:N2}", "the price is what the slot is for");

        var tight = indicator.Describe(12).Text;
        StringAssert.StartsWith(tight, "*", "with no room for the word, the marker shrinks to one character");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheInstrumentIsFoundByItsPairWhenStoredUnderAnotherName()
    {
        var instrument = Bitcoin(5m);
        instrument.Name = "BTC";
        var (indicator, _) = Create(instrument);

        StringAssert.Contains(indicator.Describe(30).Text, "BTC/USD");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DrawFillsItsWidth()
    {
        var (indicator, _) = Create(Bitcoin(1m));
        indicator.ViewSize = new iConsole.Data.Size(18, 1);
        var buffer = new iConsole.Test.Core.ScreenBuffer(18, 1);

        indicator.Draw(buffer);

        Assert.IsTrue(Enumerable.Range(0, 18).All(x => buffer.IsWritten(x, 0)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFailedAttempt_SaysUnavailableInsteadOfSpinningForever()
    {
        var (indicator, service) = Create();
        service.LastRefreshFailed = true;

        var (text, _) = indicator.Describe(30);

        Assert.AreEqual("Bitcoin unavailable", text.TrimEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFailedAttemptBeingRetried_KeepsTheSpinner()
    {
        var (indicator, service) = Create();
        service.LastRefreshFailed = true;
        service.IsRefreshing = true;

        StringAssert.StartsWith(indicator.Describe(30).Text, "Bitcoin ");
        StringAssert.DoesNotMatch(indicator.Describe(30).Text, new System.Text.RegularExpressions.Regex("unavailable"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnInstrumentWithNoValue_AfterAFailedAttempt_SaysUnavailable()
    {
        var (indicator, service) = Create(new Instrument { Id = Guid.NewGuid(), Name = "Bitcoin", Currency = "USD", InstrumentType = InstrumentType.CryptoCurrency });
        service.LastRefreshFailed = true;

        var (text, _) = indicator.Describe(30);

        Assert.AreEqual("Bitcoin unavailable", text.TrimEnd());
    }
}

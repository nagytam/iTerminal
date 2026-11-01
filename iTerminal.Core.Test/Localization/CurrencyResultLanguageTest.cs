using System.Resources;
using iConsole.Data;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Dialogs.CurrencyConverter;
using iTerminal.Core.Services.Currency;
using iTerminal.Core.Test.Services.Currency;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Localization;

/// <summary>The converter's result line follows the chosen language, with the amount and the currencies filled in. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class CurrencyResultLanguageTest
{
    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static void Speak(string code)
    {
        Localizer localizer = new(
        [
            new ResourceLocalizationSource(new ResourceManager("iConsole.Localization.Resources.Strings", typeof(Localizer).Assembly)),
            new ResourceLocalizationSource(new ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(CurrencyConverterDialog).Assembly)),
        ]);
        Assert.IsTrue(localizer.SetLanguage(code), code);
        UiText.Current = localizer;
    }

    private static CurrencyConverterDialog Create()
    {
        var settings = new CurrencyTest.MemorySettings();
        return new CurrencyConverterDialog(
            new CurrencyService(new CurrencyTest.FakeProvider(), settings), settings, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
    }

    [TestMethod]
    public void WithNoRatesYetTheLineSaysSoInGerman()
    {
        Speak("de");

        Assert.AreEqual("Noch keine Kurse – F5 lädt sie herunter.", Create().ResultLine());
    }

    [TestMethod]
    public void WithNoRatesYetTheLineIsEnglishWithNoLanguageChosen()
    {
        Assert.AreEqual("No rates yet — press F5 to download them.", Create().ResultLine());
    }

    [TestMethod]
    public async Task AnAmountThatIsNotANumberIsQuotedInGerman()
    {
        Speak("de");
        var settings = new CurrencyTest.MemorySettings();
        settings.Values[CurrencyService.RatesKey] = CurrencyTest.Sample().Serialize();
        CurrencyConverterDialog dialog = new(
            new CurrencyService(new CurrencyTest.FakeProvider(), settings), settings, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
        await ShowAsync(dialog);
        await dialog.RequestTask;
        dialog.AmountBox.Text = "abc";

        Assert.AreEqual("„abc” ist keine Zahl.", dialog.ResultLine());
    }
}

// Step bindings for Features/CurrencyAndWeather.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class CurrencyAndWeatherSteps
{
    [When(@"I convert 100 {string} to {string}")]
    public void WhenIConvert100ValueToValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the result shows an amount in euros computed from the daily rate")]
    public void ThenTheResultShowsAnAmountInEurosComputedFromTheDailyRate() => throw new PendingStepException();

    [Given(@"the rates were downloaded yesterday")]
    public void GivenTheRatesWereDownloadedYesterday() => throw new PendingStepException();

    [Given(@"the network is unavailable")]
    public void GivenTheNetworkIsUnavailable() => throw new PendingStepException();

    [Then(@"the converter shows the saved rates with their date")]
    public void ThenTheConverterShowsTheSavedRatesWithTheirDate() => throw new PendingStepException();

    [Given(@"the rates were not downloaded in this run")]
    public void GivenTheRatesWereNotDownloadedInThisRun() => throw new PendingStepException();

    [Then(@"the rates are downloaded in the background")]
    public void ThenTheRatesAreDownloadedInTheBackground() => throw new PendingStepException();

    [Then(@"the screen keeps responding while they download")]
    public void ThenTheScreenKeepsRespondingWhileTheyDownload() => throw new PendingStepException();

    [Given(@"two rate downloads were started one after the other")]
    public void GivenTwoRateDownloadsWereStartedOneAfterTheOther() => throw new PendingStepException();

    [When(@"the first one finishes last")]
    public void WhenTheFirstOneFinishesLast() => throw new PendingStepException();

    [Then(@"the converter shows the rates of the second download")]
    public void ThenTheConverterShowsTheRatesOfTheSecondDownload() => throw new PendingStepException();

    [Given(@"the weather service is available")]
    public void GivenTheWeatherServiceIsAvailable() => throw new PendingStepException();

    [Then(@"the menu bar shows the current temperature and conditions")]
    public void ThenTheMenuBarShowsTheCurrentTemperatureAndConditions() => throw new PendingStepException();

    [Given(@"the weather service is not available")]
    public void GivenTheWeatherServiceIsNotAvailable() => throw new PendingStepException();

    [Then(@"the menu bar shows that the weather is unavailable")]
    public void ThenTheMenuBarShowsThatTheWeatherIsUnavailable() => throw new PendingStepException();
}

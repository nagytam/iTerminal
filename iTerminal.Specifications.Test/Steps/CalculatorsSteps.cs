// Step bindings for Features/Calculators.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class CalculatorsSteps
{
    [Then(@"the result shows 14")]
    public void ThenTheResultShows14() => throw new PendingStepException();

    [Then(@"the result says the division is not defined")]
    public void ThenTheResultSaysTheDivisionIsNotDefined() => throw new PendingStepException();

    [When(@"I convert 10 kilometres to miles")]
    public void WhenIConvert10KilometresToMiles() => throw new PendingStepException();

    [Then(@"the result shows about 6.21 miles")]
    public void ThenTheResultShowsAbout621Miles() => throw new PendingStepException();

    [When(@"I convert 10 kilograms to metres")]
    public void WhenIConvert10KilogramsToMetres() => throw new PendingStepException();

    [Then(@"the converter says the units are of different kinds")]
    public void ThenTheConverterSaysTheUnitsAreOfDifferentKinds() => throw new PendingStepException();

    [Then(@"the calendar shows the current month with today marked")]
    public void ThenTheCalendarShowsTheCurrentMonthWithTodayMarked() => throw new PendingStepException();

    [When(@"I add the item {string}")]
    public void WhenIAddTheItemValue(string value1) => throw new PendingStepException();

    [When(@"I complete {string}")]
    public void WhenICompleteValue(string value1) => throw new PendingStepException();

    [Then(@"{string} is shown as done")]
    public void ThenValueIsShownAsDone(string value1) => throw new PendingStepException();

    [Given(@"the to-do item {string} exists")]
    public void GivenTheToDoItemValueExists(string value1) => throw new PendingStepException();

    [When(@"iTerminal is restarted and I press {string}")]
    public void WhenITerminalIsRestartedAndIPressValue(string value1) => throw new PendingStepException();

    [Then(@"{string} is listed")]
    public void ThenValueIsListed(string value1) => throw new PendingStepException();

    [Then(@"the watched symbols are listed with their latest price")]
    public void ThenTheWatchedSymbolsAreListedWithTheirLatestPrice() => throw new PendingStepException();

    [When(@"I press {string} and search for {string}")]
    public void WhenIPressValueAndSearchForValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the search lists each exchange where the company trades")]
    public void ThenTheSearchListsEachExchangeWhereTheCompanyTrades() => throw new PendingStepException();

    [When(@"I choose a result")]
    public void WhenIChooseAResult() => throw new PendingStepException();

    [Then(@"the symbol is added to the watch list")]
    public void ThenTheSymbolIsAddedToTheWatchList() => throw new PendingStepException();
}

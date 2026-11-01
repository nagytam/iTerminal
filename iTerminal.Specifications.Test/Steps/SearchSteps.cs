// Step bindings for Features/Search.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class SearchSteps
{
    [When(@"I type {string} in the name box and press {string}")]
    public void WhenITypeValueInTheNameBoxAndPressValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the results list every {string} file under the folder")]
    public void ThenTheResultsListEveryValueFileUnderTheFolder(string value1) => throw new PendingStepException();

    [Then(@"the results list the {string} and {string} files")]
    public void ThenTheResultsListTheValueAndValueFiles(string value1, string value2) => throw new PendingStepException();

    [When(@"I type {string} in the contains box and press {string}")]
    public void WhenITypeValueInTheContainsBoxAndPressValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the results list only the files that contain {string}")]
    public void ThenTheResultsListOnlyTheFilesThatContainValue(string value1) => throw new PendingStepException();

    [Given(@"a search has listed {string} and {string}")]
    public void GivenASearchHasListedValueAndValue(string value1, string value2) => throw new PendingStepException();

    [When(@"I select {string} in the results and press {string}")]
    public void WhenISelectValueInTheResultsAndPressValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} is copied")]
    public void ThenValueIsCopied(string value1) => throw new PendingStepException();

    [When(@"I open the File menu")]
    public void WhenIOpenTheFileMenu() => throw new PendingStepException();

    [Then(@"the search has {string} as the size limit")]
    public void ThenTheSearchHasValueAsTheSizeLimit(string value1) => throw new PendingStepException();

    [Then(@"the search has {string} as the age limit")]
    public void ThenTheSearchHasValueAsTheAgeLimit(string value1) => throw new PendingStepException();

    [Given(@"a search of a large folder is running")]
    public void GivenASearchOfALargeFolderIsRunning() => throw new PendingStepException();

    [Then(@"the search stops and keeps the results found so far")]
    public void ThenTheSearchStopsAndKeepsTheResultsFoundSoFar() => throw new PendingStepException();

    [Given(@"a search has run")]
    public void GivenASearchHasRun() => throw new PendingStepException();

    [Then(@"the panel's address starts with {string}")]
    public void ThenThePanelSAddressStartsWithValue(string value1) => throw new PendingStepException();

    [Then(@"the cursor moves to {string}")]
    public void ThenTheCursorMovesToValue(string value1) => throw new PendingStepException();
}

// Step bindings for Features/PanelSorting.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class PanelSortingSteps
{
    [Given(@"the left panel lists a folder and files of different sizes")]
    public void GivenTheLeftPanelListsAFolderAndFilesOfDifferentSizes() => throw new PendingStepException();

    [When(@"I choose {string} in the sort dialog")]
    public void WhenIChooseValueInTheSortDialog(string value1) => throw new PendingStepException();

    [Then(@"the left panel lists its folders before its files")]
    public void ThenTheLeftPanelListsItsFoldersBeforeItsFiles() => throw new PendingStepException();

    [Then(@"the files are ordered from the largest to the smallest")]
    public void ThenTheFilesAreOrderedFromTheLargestToTheSmallest() => throw new PendingStepException();

    [Given(@"the left panel is sorted by size")]
    public void GivenTheLeftPanelIsSortedBySize() => throw new PendingStepException();

    [Then(@"the cursor row of the left panel's first row is {string}")]
    public void ThenTheCursorRowOfTheLeftPanelSFirstRowIsValue(string value1) => throw new PendingStepException();

    [Given(@"two files of the same size are in the left panel")]
    public void GivenTwoFilesOfTheSameSizeAreInTheLeftPanel() => throw new PendingStepException();

    [Then(@"the two files are listed by name")]
    public void ThenTheTwoFilesAreListedByName() => throw new PendingStepException();

    [Then(@"the footer shows {string}")]
    public void ThenTheFooterShowsValue(string value1) => throw new PendingStepException();

    [Then(@"the left panel is still sorted by size")]
    public void ThenTheLeftPanelIsStillSortedBySize() => throw new PendingStepException();

    [Given(@"the left panel lists {string}, {string} and {string}")]
    public void GivenTheLeftPanelListsValueValueAndValue(string value1, string value2, string value3) => throw new PendingStepException();

    [Given(@"{string} is on in the Panels settings")]
    public void GivenValueIsOnInThePanelsSettings(string value1) => throw new PendingStepException();

    [Then(@"the files are listed as {string}, {string}, {string}")]
    public void ThenTheFilesAreListedAsValueValueValue(string value1, string value2, string value3) => throw new PendingStepException();

    [When(@"I choose {string} for the left panel")]
    public void WhenIChooseValueForTheLeftPanel(string value1) => throw new PendingStepException();
}

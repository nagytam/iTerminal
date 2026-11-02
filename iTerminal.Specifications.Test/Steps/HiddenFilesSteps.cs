// Step bindings for Features/HiddenFiles.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class HiddenFilesSteps
{
    [Given(@"the left panel contains a hidden file {string}")]
    public void GivenTheLeftPanelContainsAHiddenFileValue(string value1) => throw new PendingStepException();

    [When(@"I open the settings dialog on the Panels page")]
    public void WhenIOpenTheSettingsDialogOnThePanelsPage() => throw new PendingStepException();

    [When(@"I tick {string}")]
    public void WhenITickValue(string value1) => throw new PendingStepException();

    [Given(@"the left panel shows a hidden folder")]
    public void GivenTheLeftPanelShowsAHiddenFolder() => throw new PendingStepException();

    [When(@"I move the cursor into the hidden folder")]
    public void WhenIMoveTheCursorIntoTheHiddenFolder() => throw new PendingStepException();

    [Then(@"the cursor row is {string}")]
    public void ThenTheCursorRowIsValue(string value1) => throw new PendingStepException();

    [Then(@"I can press Enter to go back to its parent")]
    public void ThenICanPressEnterToGoBackToItsParent() => throw new PendingStepException();

    [Given(@"a hidden file {string} is in the left panel")]
    public void GivenAHiddenFileValueIsInTheLeftPanel(string value1) => throw new PendingStepException();

    [When(@"I select all entries of the left panel and press {string}")]
    public void WhenISelectAllEntriesOfTheLeftPanelAndPressValue(string value1) => throw new PendingStepException();

    [Then(@"the copy includes {string}")]
    public void ThenTheCopyIncludesValue(string value1) => throw new PendingStepException();
}

// Step bindings for Features/RecycleBin.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class RecycleBinSteps
{
    [Given(@"{string} was deleted to the recycle bin")]
    public void GivenValueWasDeletedToTheRecycleBin(string value1) => throw new PendingStepException();

    [Given(@"the focused panel shows the recycle bin")]
    public void GivenTheFocusedPanelShowsTheRecycleBin() => throw new PendingStepException();

    [When(@"I move the cursor to {string} and choose {string}")]
    public void WhenIMoveTheCursorToValueAndChooseValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} is back in its original folder")]
    public void ThenValueIsBackInItsOriginalFolder(string value1) => throw new PendingStepException();

    [When(@"I move the cursor to {string} and delete it for good")]
    public void WhenIMoveTheCursorToValueAndDeleteItForGood(string value1) => throw new PendingStepException();

    [Then(@"{string} is no longer in the recycle bin")]
    public void ThenValueIsNoLongerInTheRecycleBin(string value1) => throw new PendingStepException();

    [Then(@"the panel shows the folder it showed before")]
    public void ThenThePanelShowsTheFolderItShowedBefore() => throw new PendingStepException();

    [Given(@"{string} was deleted after {string}")]
    public void GivenValueWasDeletedAfterValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} is listed before {string}")]
    public void ThenValueIsListedBeforeValue(string value1, string value2) => throw new PendingStepException();
}

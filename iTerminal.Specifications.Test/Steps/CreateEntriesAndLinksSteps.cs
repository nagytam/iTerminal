// Step bindings for Features/CreateEntriesAndLinks.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class CreateEntriesAndLinksSteps
{
    [Then(@"{string} is empty")]
    public void ThenValueIsEmpty(string value1) => throw new PendingStepException();

    [Then(@"the dialog is still open")]
    public void ThenTheDialogIsStillOpen() => throw new PendingStepException();

    [Then(@"the dialog says that {string} already exists")]
    public void ThenTheDialogSaysThatValueAlreadyExists(string value1) => throw new PendingStepException();

    [Then(@"a symbolic link named {string} is offered in the right panel's folder")]
    public void ThenASymbolicLinkNamedValueIsOfferedInTheRightPanelSFolder(string value1) => throw new PendingStepException();

    [Then(@"the dialog says that hard links are for files only")]
    public void ThenTheDialogSaysThatHardLinksAreForFilesOnly() => throw new PendingStepException();

    [Given(@"a symbolic link {string} to {string} is in the left panel")]
    public void GivenASymbolicLinkValueToValueIsInTheLeftPanel(string value1, string value2) => throw new PendingStepException();

    [Given(@"a symbolic link {string} to {string} is under the cursor")]
    public void GivenASymbolicLinkValueToValueIsUnderTheCursor(string value1, string value2) => throw new PendingStepException();

    [When(@"I type {string} as the new target")]
    public void WhenITypeValueAsTheNewTarget(string value1) => throw new PendingStepException();

    [Then(@"{string} points to {string}")]
    public void ThenValuePointsToValue(string value1, string value2) => throw new PendingStepException();

    [Given(@"{string} has one name")]
    public void GivenValueHasOneName(string value1) => throw new PendingStepException();

    [Then(@"the dialog says the file has only one name")]
    public void ThenTheDialogSaysTheFileHasOnlyOneName() => throw new PendingStepException();
}

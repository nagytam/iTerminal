// Step bindings for Features/DialogBasics.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class DialogBasicsSteps
{
    [When(@"I press {string} and then {string}")]
    public void WhenIPressValueAndThenValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the copy dialog is closed")]
    public void ThenTheCopyDialogIsClosed() => throw new PendingStepException();

    [Then(@"no file was copied")]
    public void ThenNoFileWasCopied() => throw new PendingStepException();

    [Then(@"the focus moves to the next control of the dialog")]
    public void ThenTheFocusMovesToTheNextControlOfTheDialog() => throw new PendingStepException();

    [Then(@"the copy starts")]
    public void ThenTheCopyStarts() => throw new PendingStepException();

    [Given(@"the copy dialog is open")]
    public void GivenTheCopyDialogIsOpen() => throw new PendingStepException();

    [Then(@"the cursor of the panel behind the dialog does not move")]
    public void ThenTheCursorOfThePanelBehindTheDialogDoesNotMove() => throw new PendingStepException();

    [Then(@"a help page lists the keys that work in the copy dialog")]
    public void ThenAHelpPageListsTheKeysThatWorkInTheCopyDialog() => throw new PendingStepException();

    [Given(@"the rename dialog is open with {string}")]
    public void GivenTheRenameDialogIsOpenWithValue(string value1) => throw new PendingStepException();

    [Then(@"the name is {string}")]
    public void ThenTheNameIsValue(string value1) => throw new PendingStepException();

    [When(@"I select all and press {string}")]
    public void WhenISelectAllAndPressValue(string value1) => throw new PendingStepException();

    [When(@"I clear the text and press {string}")]
    public void WhenIClearTheTextAndPressValue(string value1) => throw new PendingStepException();

    [Then(@"the footer shows the keys of the copy dialog")]
    public void ThenTheFooterShowsTheKeysOfTheCopyDialog() => throw new PendingStepException();
}

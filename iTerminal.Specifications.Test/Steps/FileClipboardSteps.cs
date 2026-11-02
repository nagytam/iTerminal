// Step bindings for Features/FileClipboard.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class FileClipboardSteps
{
    [When(@"I move the left panel into {string}")]
    public void WhenIMoveTheLeftPanelIntoValue(string value1) => throw new PendingStepException();

    [Then(@"{string} is in {string}")]
    public void ThenValueIsInValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} is still in the original folder")]
    public void ThenValueIsStillInTheOriginalFolder(string value1) => throw new PendingStepException();

    [Then(@"{string} is still in the left panel")]
    public void ThenValueIsStillInTheLeftPanel(string value1) => throw new PendingStepException();

    [When(@"I move the right panel into {string} and press {string}")]
    public void WhenIMoveTheRightPanelIntoValueAndPressValue(string value1, string value2) => throw new PendingStepException();

    [Given(@"{string} is copied with Ctrl+C")]
    public void GivenValueIsCopiedWithCtrlC(string value1) => throw new PendingStepException();

    [When(@"I press {string} in the same folder")]
    public void WhenIPressValueInTheSameFolder(string value1) => throw new PendingStepException();

    [Then(@"the new file is named {string}")]
    public void ThenTheNewFileIsNamedValue(string value1) => throw new PendingStepException();

    [Given(@"{string} is cut with Ctrl+X")]
    public void GivenValueIsCutWithCtrlX(string value1) => throw new PendingStepException();

    [Given(@"{string} is copied and a file of that name exists in the target")]
    public void GivenValueIsCopiedAndAFileOfThatNameExistsInTheTarget(string value1) => throw new PendingStepException();

    [When(@"I press {string} in the target")]
    public void WhenIPressValueInTheTarget(string value1) => throw new PendingStepException();

    [Then(@"the paste reports the name is taken and the existing file is kept")]
    public void ThenThePasteReportsTheNameIsTakenAndTheExistingFileIsKept() => throw new PendingStepException();

    [Given(@"files were copied in another program")]
    public void GivenFilesWereCopiedInAnotherProgram() => throw new PendingStepException();

    [When(@"I press {string} in a panel")]
    public void WhenIPressValueInAPanel(string value1) => throw new PendingStepException();

    [Then(@"the files are copied into the panel's folder")]
    public void ThenTheFilesAreCopiedIntoThePanelSFolder() => throw new PendingStepException();

    [Then(@"{string} is on the clipboard")]
    public void ThenValueIsOnTheClipboard(string value1) => throw new PendingStepException();

    [Then(@"iTerminal keeps running")]
    public void ThenITerminalKeepsRunning() => throw new PendingStepException();

    [Then(@"iTerminal closes")]
    public void ThenITerminalCloses() => throw new PendingStepException();
}

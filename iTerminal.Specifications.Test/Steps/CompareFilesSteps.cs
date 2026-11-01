// Step bindings for Features/CompareFiles.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class CompareFilesSteps
{
    [Given(@"exactly two files are marked in the left panel")]
    public void GivenExactlyTwoFilesAreMarkedInTheLeftPanel() => throw new PendingStepException();

    [Then(@"the diff view shows both files side by side")]
    public void ThenTheDiffViewShowsBothFilesSideBySide() => throw new PendingStepException();

    [Given(@"the cursor is on {string} in the left panel")]
    public void GivenTheCursorIsOnValueInTheLeftPanel(string value1) => throw new PendingStepException();

    [Given(@"the cursor is on {string} in the right panel")]
    public void GivenTheCursorIsOnValueInTheRightPanel(string value1) => throw new PendingStepException();

    [Then(@"{string} and {string} are compared")]
    public void ThenValueAndValueAreCompared(string value1, string value2) => throw new PendingStepException();

    [Given(@"the diff view shows several differences")]
    public void GivenTheDiffViewShowsSeveralDifferences() => throw new PendingStepException();

    [Then(@"the next difference is at the cursor")]
    public void ThenTheNextDifferenceIsAtTheCursor() => throw new PendingStepException();

    [Then(@"the previous difference is at the cursor")]
    public void ThenThePreviousDifferenceIsAtTheCursor() => throw new PendingStepException();

    [Given(@"two files differ only in spaces")]
    public void GivenTwoFilesDifferOnlyInSpaces() => throw new PendingStepException();

    [Then(@"the files are shown as the same")]
    public void ThenTheFilesAreShownAsTheSame() => throw new PendingStepException();

    [Given(@"the diff view shows text")]
    public void GivenTheDiffViewShowsText() => throw new PendingStepException();

    [Then(@"the files are shown as bytes side by side")]
    public void ThenTheFilesAreShownAsBytesSideBySide() => throw new PendingStepException();

    [Then(@"the differing bytes are highlighted")]
    public void ThenTheDifferingBytesAreHighlighted() => throw new PendingStepException();

    [Given(@"a file larger than 8 MB is marked")]
    public void GivenAFileLargerThan8MBIsMarked() => throw new PendingStepException();

    [Then(@"the diff view says the file is too large to compare")]
    public void ThenTheDiffViewSaysTheFileIsTooLargeToCompare() => throw new PendingStepException();

    [When(@"I right-click {string}")]
    public void WhenIRightClickValue(string value1) => throw new PendingStepException();

    [Then(@"the context menu lists the file's programs and the panel's commands")]
    public void ThenTheContextMenuListsTheFileSProgramsAndThePanelSCommands() => throw new PendingStepException();
}

// Step bindings for Features/CopyAndMove.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class CopyAndMoveSteps
{
    [Given(@"the right panel shows {string}")]
    public void GivenTheRightPanelShowsValue(string value1) => throw new PendingStepException();

    [Then(@"the copy dialog is shown")]
    public void ThenTheCopyDialogIsShown() => throw new PendingStepException();

    [Then(@"its target folder is the folder of the right panel")]
    public void ThenItsTargetFolderIsTheFolderOfTheRightPanel() => throw new PendingStepException();

    [Then(@"a progress window for the copy is shown")]
    public void ThenAProgressWindowForTheCopyIsShown() => throw new PendingStepException();

    [Then(@"the task appears in the task manager")]
    public void ThenTheTaskAppearsInTheTaskManager() => throw new PendingStepException();

    [When(@"I copy {string} from the left panel to the right panel")]
    public void WhenICopyValueFromTheLeftPanelToTheRightPanel(string value1) => throw new PendingStepException();

    [Then(@"a completion notification is shown")]
    public void ThenACompletionNotificationIsShown() => throw new PendingStepException();

    [Given(@"{string} was last modified on 2020-01-02 10:00")]
    public void GivenValueWasLastModifiedOn202001021000(string value1) => throw new PendingStepException();

    [When(@"I copy {string} to the right panel")]
    public void WhenICopyValueToTheRightPanel(string value1) => throw new PendingStepException();

    [Then(@"the copy of {string} was last modified on 2020-01-02 10:00")]
    public void ThenTheCopyOfValueWasLastModifiedOn202001021000(string value1) => throw new PendingStepException();

    [Given(@"{string} also exists in the target folder")]
    public void GivenValueAlsoExistsInTheTargetFolder(string value1) => throw new PendingStepException();

    [Then(@"a conflict prompt offers Overwrite, Skip, Rename, Keep newer and Keep larger")]
    public void ThenAConflictPromptOffersOverwriteSkipRenameKeepNewerAndKeepLarger() => throw new PendingStepException();

    [Given(@"a conflict prompt is shown for {string}")]
    public void GivenAConflictPromptIsShownForValue(string value1) => throw new PendingStepException();

    [Then(@"the file in the target folder is unchanged")]
    public void ThenTheFileInTheTargetFolderIsUnchanged() => throw new PendingStepException();

    [Then(@"the source file is kept")]
    public void ThenTheSourceFileIsKept() => throw new PendingStepException();

    [Then(@"the copy is named {string} in the target folder")]
    public void ThenTheCopyIsNamedValueInTheTargetFolder(string value1) => throw new PendingStepException();

    [Given(@"a conflict prompt is shown for {string} in a run of five files")]
    public void GivenAConflictPromptIsShownForValueInARunOfFiveFiles(string value1) => throw new PendingStepException();

    [When(@"I choose {string} and tick {string}")]
    public void WhenIChooseValueAndTickValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"no further conflict prompt is shown in that run")]
    public void ThenNoFurtherConflictPromptIsShownInThatRun() => throw new PendingStepException();

    [Given(@"a conflict prompt is shown")]
    public void GivenAConflictPromptIsShown() => throw new PendingStepException();

    [Then(@"the steps after the current one are not run")]
    public void ThenTheStepsAfterTheCurrentOneAreNotRun() => throw new PendingStepException();

    [Given(@"one of five files cannot be read")]
    public void GivenOneOfFiveFilesCannotBeRead() => throw new PendingStepException();

    [When(@"I copy the five files to the right panel")]
    public void WhenICopyTheFiveFilesToTheRightPanel() => throw new PendingStepException();

    [Then(@"the other four files are copied")]
    public void ThenTheOtherFourFilesAreCopied() => throw new PendingStepException();

    [Then(@"the failure is listed when the run ends")]
    public void ThenTheFailureIsListedWhenTheRunEnds() => throw new PendingStepException();

    [Given(@"the target drive has less free space than the source needs")]
    public void GivenTheTargetDriveHasLessFreeSpaceThanTheSourceNeeds() => throw new PendingStepException();

    [Then(@"a prompt asks whether to pause or go on anyway")]
    public void ThenAPromptAsksWhetherToPauseOrGoOnAnyway() => throw new PendingStepException();

    [When(@"I move {string} from the left panel to the right panel with {string}")]
    public void WhenIMoveValueFromTheLeftPanelToTheRightPanelWithValue(string value1, string value2) => throw new PendingStepException();

    [Given(@"the source and the target are on the same volume")]
    public void GivenTheSourceAndTheTargetAreOnTheSameVolume() => throw new PendingStepException();

    [When(@"I move {string} with {string}")]
    public void WhenIMoveValueWithValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"no free space check is shown")]
    public void ThenNoFreeSpaceCheckIsShown() => throw new PendingStepException();

    [Given(@"the target folder is inside the folder being copied")]
    public void GivenTheTargetFolderIsInsideTheFolderBeingCopied() => throw new PendingStepException();

    [When(@"I copy {string} to its own subfolder")]
    public void WhenICopyValueToItsOwnSubfolder(string value1) => throw new PendingStepException();

    [Then(@"the copy is refused with a message")]
    public void ThenTheCopyIsRefusedWithAMessage() => throw new PendingStepException();

    [Then(@"the copy dialog is not shown")]
    public void ThenTheCopyDialogIsNotShown() => throw new PendingStepException();
}

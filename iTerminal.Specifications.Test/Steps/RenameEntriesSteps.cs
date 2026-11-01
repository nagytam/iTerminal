// Step bindings for Features/RenameEntries.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class RenameEntriesSteps
{
    [Then(@"the rename dialog stays open")]
    public void ThenTheRenameDialogStaysOpen() => throw new PendingStepException();

    [When(@"I enter the pattern {string}")]
    public void WhenIEnterThePatternValue(string value1) => throw new PendingStepException();

    [Then(@"the preview shows {string} becoming {string}")]
    public void ThenThePreviewShowsValueBecomingValue(string value1, string value2) => throw new PendingStepException();

    [When(@"I enter the pattern {string} with Find {string} and Replace {string}")]
    public void WhenIEnterThePatternValueWithFindValueAndReplaceValue(string value1, string value2, string value3) => throw new PendingStepException();

    [Given(@"{string} and {string} are marked and {string} already exists")]
    public void GivenValueAndValueAreMarkedAndValueAlreadyExists(string value1, string value2, string value3) => throw new PendingStepException();

    [Then(@"the preview marks the row for {string} as a conflict and does not rename it")]
    public void ThenThePreviewMarksTheRowForValueAsAConflictAndDoesNotRenameIt(string value1) => throw new PendingStepException();

    [Then(@"the preview lists only {string}")]
    public void ThenThePreviewListsOnlyValue(string value1) => throw new PendingStepException();

    [Given(@"the left panel shows the inside of an archive")]
    public void GivenTheLeftPanelShowsTheInsideOfAnArchive() => throw new PendingStepException();

    [Then(@"{string} is not available for the archive panel")]
    public void ThenValueIsNotAvailableForTheArchivePanel(string value1) => throw new PendingStepException();

    [Given(@"the preview lists five files ready to rename")]
    public void GivenThePreviewListsFiveFilesReadyToRename() => throw new PendingStepException();

    [When(@"I confirm the multi-rename")]
    public void WhenIConfirmTheMultiRename() => throw new PendingStepException();

    [Then(@"the five files are renamed as the preview showed")]
    public void ThenTheFiveFilesAreRenamedAsThePreviewShowed() => throw new PendingStepException();

    [Then(@"a file that changed on disk in the meantime is listed as an error")]
    public void ThenAFileThatChangedOnDiskInTheMeantimeIsListedAsAnError() => throw new PendingStepException();
}

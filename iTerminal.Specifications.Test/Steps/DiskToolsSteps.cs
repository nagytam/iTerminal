// Step bindings for Features/DiskTools.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class DiskToolsSteps
{
    [Then(@"the disk usage tree shows the folders with their sizes")]
    public void ThenTheDiskUsageTreeShowsTheFoldersWithTheirSizes() => throw new PendingStepException();

    [Given(@"{string} contains a link to a large folder elsewhere")]
    public void GivenValueContainsALinkToALargeFolderElsewhere(string value1) => throw new PendingStepException();

    [When(@"the disk usage of {string} is measured")]
    public void WhenTheDiskUsageOfValueIsMeasured(string value1) => throw new PendingStepException();

    [Then(@"the linked folder's size is not counted")]
    public void ThenTheLinkedFolderSSizeIsNotCounted() => throw new PendingStepException();

    [Given(@"the disk usage tree shows {string}")]
    public void GivenTheDiskUsageTreeShowsValue(string value1) => throw new PendingStepException();

    [Then(@"the panel shows {string}")]
    public void ThenThePanelShowsValue(string value1) => throw new PendingStepException();

    [Given(@"{string} and {string} have identical content")]
    public void GivenValueAndValueHaveIdenticalContent(string value1, string value2) => throw new PendingStepException();

    [When(@"I press {string} in the folder")]
    public void WhenIPressValueInTheFolder(string value1) => throw new PendingStepException();

    [Then(@"the duplicate groups list {string} and {string}")]
    public void ThenTheDuplicateGroupsListValueAndValue(string value1, string value2) => throw new PendingStepException();

    [Given(@"{string} and {string} have the same size but different content")]
    public void GivenValueAndValueHaveTheSameSizeButDifferentContent(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} and {string} are not in the same group")]
    public void ThenValueAndValueAreNotInTheSameGroup(string value1, string value2) => throw new PendingStepException();

    [Then(@"the dialog says {string} will be destroyed")]
    public void ThenTheDialogSaysValueWillBeDestroyed(string value1) => throw new PendingStepException();

    [Then(@"{string} is overwritten, renamed and deleted")]
    public void ThenValueIsOverwrittenRenamedAndDeleted(string value1) => throw new PendingStepException();

    [Given(@"the secure wipe has armed {string}")]
    public void GivenTheSecureWipeHasArmedValue(string value1) => throw new PendingStepException();

    [Then(@"the wipe is set to seven passes")]
    public void ThenTheWipeIsSetToSevenPasses() => throw new PendingStepException();

    [Given(@"{string} is a link to {string}")]
    public void GivenValueIsALinkToValue(string value1, string value2) => throw new PendingStepException();

    [When(@"I wipe {string}")]
    public void WhenIWipeValue(string value1) => throw new PendingStepException();

    [Then(@"the dialog says that SSDs, snapshots and backups may keep copies")]
    public void ThenTheDialogSaysThatSSDsSnapshotsAndBackupsMayKeepCopies() => throw new PendingStepException();
}

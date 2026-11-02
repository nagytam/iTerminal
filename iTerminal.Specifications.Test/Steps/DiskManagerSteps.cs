// Step bindings for Features/DiskManager.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class DiskManagerSteps
{
    [Then(@"the drives are listed with their label, type, file system and free space")]
    public void ThenTheDrivesAreListedWithTheirLabelTypeFileSystemAndFreeSpace() => throw new PendingStepException();

    [Given(@"a USB drive {string} is under the cursor")]
    public void GivenAUSBDriveValueIsUnderTheCursor(string value1) => throw new PendingStepException();

    [Then(@"the drive is ejected")]
    public void ThenTheDriveIsEjected() => throw new PendingStepException();

    [Then(@"a notification says it can be removed safely")]
    public void ThenANotificationSaysItCanBeRemovedSafely() => throw new PendingStepException();

    [Given(@"the drive {string} has an open file")]
    public void GivenTheDriveValueHasAnOpenFile(string value1) => throw new PendingStepException();

    [Then(@"the message says the drive is in use")]
    public void ThenTheMessageSaysTheDriveIsInUse() => throw new PendingStepException();

    [Then(@"the format dialog asks for the drive letter to be typed")]
    public void ThenTheFormatDialogAsksForTheDriveLetterToBeTyped() => throw new PendingStepException();

    [Then(@"the drive is formatted")]
    public void ThenTheDriveIsFormatted() => throw new PendingStepException();

    [Given(@"the system drive is under the cursor")]
    public void GivenTheSystemDriveIsUnderTheCursor() => throw new PendingStepException();

    [Then(@"the format is refused before the dialog opens")]
    public void ThenTheFormatIsRefusedBeforeTheDialogOpens() => throw new PendingStepException();

    [Given(@"the left panel is on {string}")]
    public void GivenTheLeftPanelIsOnValue(string value1) => throw new PendingStepException();

    [When(@"I press {string} on {string}")]
    public void WhenIPressValueOnValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the format is refused because a panel is on that drive")]
    public void ThenTheFormatIsRefusedBecauseAPanelIsOnThatDrive() => throw new PendingStepException();

    [Then(@"the mount dialog is filled with {string}")]
    public void ThenTheMountDialogIsFilledWithValue(string value1) => throw new PendingStepException();

    [When(@"I confirm the mount")]
    public void WhenIConfirmTheMount() => throw new PendingStepException();

    [Then(@"the image appears as a drive")]
    public void ThenTheImageAppearsAsADrive() => throw new PendingStepException();

    [Then(@"the left panel lists the contents of {string}")]
    public void ThenTheLeftPanelListsTheContentsOfValue(string value1) => throw new PendingStepException();

    [Given(@"the drive {string} is under the cursor")]
    public void GivenTheDriveValueIsUnderTheCursor(string value1) => throw new PendingStepException();

    [Then(@"the BitLocker status of {string} is shown")]
    public void ThenTheBitLockerStatusOfValueIsShown(string value1) => throw new PendingStepException();

    [When(@"I eject a drive")]
    public void WhenIEjectADrive() => throw new PendingStepException();

    [Then(@"the dialog says {string} until it finishes")]
    public void ThenTheDialogSaysValueUntilItFinishes(string value1) => throw new PendingStepException();

    [Then(@"the outcome is also shown as a notification")]
    public void ThenTheOutcomeIsAlsoShownAsANotification() => throw new PendingStepException();
}

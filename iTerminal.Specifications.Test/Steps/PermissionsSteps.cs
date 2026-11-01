// Step bindings for Features/Permissions.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class PermissionsSteps
{
    [Given(@"the permissions dialog is open for {string}")]
    public void GivenThePermissionsDialogIsOpenForValue(string value1) => throw new PendingStepException();

    [Then(@"the dialog lists each user or group with its rights")]
    public void ThenTheDialogListsEachUserOrGroupWithItsRights() => throw new PendingStepException();

    [Given(@"{string} has {string} on {string}")]
    public void GivenValueHasValueOnValue(string value1, string value2, string value3) => throw new PendingStepException();

    [When(@"I set {string} to {string}")]
    public void WhenISetValueToValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} has no second entry")]
    public void ThenValueHasNoSecondEntry(string value1) => throw new PendingStepException();

    [Given(@"the permissions dialog shows an inherited entry")]
    public void GivenThePermissionsDialogShowsAnInheritedEntry() => throw new PendingStepException();

    [Then(@"the inherited entry cannot be removed")]
    public void ThenTheInheritedEntryCannotBeRemoved() => throw new PendingStepException();

    [Given(@"{string} inherits its rights from its folder")]
    public void GivenValueInheritsItsRightsFromItsFolder(string value1) => throw new PendingStepException();

    [When(@"I press {string} and turn inheritance off")]
    public void WhenIPressValueAndTurnInheritanceOff(string value1) => throw new PendingStepException();

    [Then(@"the inherited rights are kept as explicit entries")]
    public void ThenTheInheritedRightsAreKeptAsExplicitEntries() => throw new PendingStepException();

    [When(@"I press {string} to change the owner")]
    public void WhenIPressValueToChangeTheOwner(string value1) => throw new PendingStepException();

    [When(@"I choose a new owner")]
    public void WhenIChooseANewOwner() => throw new PendingStepException();

    [Then(@"the owner changes or the dialog says the right is missing")]
    public void ThenTheOwnerChangesOrTheDialogSaysTheRightIsMissing() => throw new PendingStepException();

    [Given(@"the permissions dialog is open on a Unix system")]
    public void GivenThePermissionsDialogIsOpenOnAUnixSystem() => throw new PendingStepException();

    [Then(@"the owner, group, mask and other entries cannot be removed")]
    public void ThenTheOwnerGroupMaskAndOtherEntriesCannotBeRemoved() => throw new PendingStepException();

    [When(@"I add the entry name {string}")]
    public void WhenIAddTheEntryNameValue(string value1) => throw new PendingStepException();

    [Then(@"the name is refused")]
    public void ThenTheNameIsRefused() => throw new PendingStepException();

    [Given(@"the current account may not change {string}")]
    public void GivenTheCurrentAccountMayNotChangeValue(string value1) => throw new PendingStepException();

    [Then(@"iTerminal offers to restart with administrator rights")]
    public void ThenITerminalOffersToRestartWithAdministratorRights() => throw new PendingStepException();
}

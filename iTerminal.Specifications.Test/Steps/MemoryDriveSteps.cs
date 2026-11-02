// Step bindings for Features/MemoryDrive.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class MemoryDriveSteps
{
    [When(@"I choose the Places page")]
    public void WhenIChooseThePlacesPage() => throw new PendingStepException();

    [Then(@"no new file appears on the disk")]
    public void ThenNoNewFileAppearsOnTheDisk() => throw new PendingStepException();

    [Given(@"{string} contains a folder {string}")]
    public void GivenValueContainsAFolderValue(string value1, string value2) => throw new PendingStepException();

    [When(@"I move {string} into {string}")]
    public void WhenIMoveValueIntoValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the move is refused before anything is removed")]
    public void ThenTheMoveIsRefusedBeforeAnythingIsRemoved() => throw new PendingStepException();
}

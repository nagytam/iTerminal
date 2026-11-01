// Step bindings for Features/UserMenu.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class UserMenuSteps
{
    [Given(@"the user menu file holds the entry {string} with the command {string}")]
    public void GivenTheUserMenuFileHoldsTheEntryValueWithTheCommandValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} runs in the command log")]
    public void ThenValueRunsInTheCommandLog(string value1) => throw new PendingStepException();

    [Then(@"the menu is read from {string} in the data folder")]
    public void ThenTheMenuIsReadFromValueInTheDataFolder(string value1) => throw new PendingStepException();

    [Given(@"the user menu holds an entry {string} with the command {string}")]
    public void GivenTheUserMenuHoldsAnEntryValueWithTheCommandValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the command runs with {string}")]
    public void ThenTheCommandRunsWithValue(string value1) => throw new PendingStepException();
}

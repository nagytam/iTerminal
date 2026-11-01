// Step bindings for Features/OpenWithAndEditWith.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class OpenWithAndEditWithSteps
{
    [Then(@"the Open With dialog lists the programs for {string} files")]
    public void ThenTheOpenWithDialogListsTheProgramsForValueFiles(string value1) => throw new PendingStepException();

    [Given(@"the Open With dialog is shown for {string}")]
    public void GivenTheOpenWithDialogIsShownForValue(string value1) => throw new PendingStepException();

    [When(@"I press {string} and add the program {string} for {string}")]
    public void WhenIPressValueAndAddTheProgramValueForValue(string value1, string value2, string value3) => throw new PendingStepException();

    [Then(@"{string} is the first program for {string} files")]
    public void ThenValueIsTheFirstProgramForValueFiles(string value1, string value2) => throw new PendingStepException();

    [Given(@"the Open With dialog lists {string} and {string}")]
    public void GivenTheOpenWithDialogListsValueAndValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"Enter on a {string} file runs {string}")]
    public void ThenEnterOnAValueFileRunsValue(string value1, string value2) => throw new PendingStepException();

    [Given(@"the Open With dialog lists {string}")]
    public void GivenTheOpenWithDialogListsValue(string value1) => throw new PendingStepException();

    [Then(@"the footer asks whether to remove {string}")]
    public void ThenTheFooterAsksWhetherToRemoveValue(string value1) => throw new PendingStepException();

    [Then(@"{string} is no longer listed")]
    public void ThenValueIsNoLongerListed(string value1) => throw new PendingStepException();

    [Given(@"no program is chosen for {string} files")]
    public void GivenNoProgramIsChosenForValueFiles(string value1) => throw new PendingStepException();

    [Then(@"the system default program opens {string}")]
    public void ThenTheSystemDefaultProgramOpensValue(string value1) => throw new PendingStepException();

    [Given(@"{string} is executable")]
    public void GivenValueIsExecutable(string value1) => throw new PendingStepException();

    [Then(@"{string} runs in the command log from its own folder")]
    public void ThenValueRunsInTheCommandLogFromItsOwnFolder(string value1) => throw new PendingStepException();

    [Given(@"{string} has a default edit program {string}")]
    public void GivenValueHasADefaultEditProgramValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} opens {string}")]
    public void ThenValueOpensValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the built-in editor is listed among the programs")]
    public void ThenTheBuiltInEditorIsListedAmongThePrograms() => throw new PendingStepException();

    [When(@"I press {string} on an entry")]
    public void WhenIPressValueOnAnEntry(string value1) => throw new PendingStepException();

    [Then(@"the dialog says a program can only open a file on this machine")]
    public void ThenTheDialogSaysAProgramCanOnlyOpenAFileOnThisMachine() => throw new PendingStepException();

    [Given(@"{string} is the default program for {string} files")]
    public void GivenValueIsTheDefaultProgramForValueFiles(string value1, string value2) => throw new PendingStepException();

    [Then(@"a notification says that {string} is not installed")]
    public void ThenANotificationSaysThatValueIsNotInstalled(string value1) => throw new PendingStepException();
}

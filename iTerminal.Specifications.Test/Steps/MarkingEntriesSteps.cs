// Step bindings for Features/MarkingEntries.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class MarkingEntriesSteps
{
    [Then(@"{string} is marked")]
    public void ThenValueIsMarked(string value1) => throw new PendingStepException();

    [Then(@"{string} is not marked")]
    public void ThenValueIsNotMarked(string value1) => throw new PendingStepException();

    [Then(@"every {string} file in the left panel is marked")]
    public void ThenEveryValueFileInTheLeftPanelIsMarked(string value1) => throw new PendingStepException();

    [Given(@"every file in the left panel is marked")]
    public void GivenEveryFileInTheLeftPanelIsMarked() => throw new PendingStepException();

    [Then(@"no {string} file in the left panel is marked")]
    public void ThenNoValueFileInTheLeftPanelIsMarked(string value1) => throw new PendingStepException();

    [Then(@"the dialog says that no entry matches the pattern")]
    public void ThenTheDialogSaysThatNoEntryMatchesThePattern() => throw new PendingStepException();

    [Then(@"every other file is marked")]
    public void ThenEveryOtherFileIsMarked() => throw new PendingStepException();

    [Then(@"every entry of the left panel is marked")]
    public void ThenEveryEntryOfTheLeftPanelIsMarked() => throw new PendingStepException();

    [Then(@"no entry of the left panel is marked")]
    public void ThenNoEntryOfTheLeftPanelIsMarked() => throw new PendingStepException();

    [Then(@"the copy dialog lists {string} and {string}")]
    public void ThenTheCopyDialogListsValueAndValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the copy dialog does not list {string}")]
    public void ThenTheCopyDialogDoesNotListValue(string value1) => throw new PendingStepException();

    [Then(@"the delete dialog lists {string}")]
    public void ThenTheDeleteDialogListsValue(string value1) => throw new PendingStepException();

    [Then(@"the footer shows that 2 entries are marked")]
    public void ThenTheFooterShowsThat2EntriesAreMarked() => throw new PendingStepException();
}

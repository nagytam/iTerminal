// Step bindings for Features/DeleteEntries.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class DeleteEntriesSteps
{
    [Then(@"the delete dialog names {string}")]
    public void ThenTheDeleteDialogNamesValue(string value1) => throw new PendingStepException();

    [Then(@"nothing has been deleted yet")]
    public void ThenNothingHasBeenDeletedYet() => throw new PendingStepException();

    [Then(@"the {string} box is ticked")]
    public void ThenTheValueBoxIsTicked(string value1) => throw new PendingStepException();

    [Given(@"{string} contains a folder link to {string} with files")]
    public void GivenValueContainsAFolderLinkToValueWithFiles(string value1, string value2) => throw new PendingStepException();

    [When(@"I delete {string} recursively")]
    public void WhenIDeleteValueRecursively(string value1) => throw new PendingStepException();

    [Then(@"the files of {string} still exist")]
    public void ThenTheFilesOfValueStillExist(string value1) => throw new PendingStepException();

    [Then(@"the link itself is deleted")]
    public void ThenTheLinkItselfIsDeleted() => throw new PendingStepException();

    [Then(@"{string} is not in the recycle bin")]
    public void ThenValueIsNotInTheRecycleBin(string value1) => throw new PendingStepException();

    [Then(@"{string} still exists")]
    public void ThenValueStillExists(string value1) => throw new PendingStepException();

    [Then(@"the delete dialog is not shown")]
    public void ThenTheDeleteDialogIsNotShown() => throw new PendingStepException();

    [Then(@"the archive is not changed")]
    public void ThenTheArchiveIsNotChanged() => throw new PendingStepException();
}

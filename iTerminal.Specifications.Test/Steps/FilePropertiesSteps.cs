// Step bindings for Features/FileProperties.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class FilePropertiesSteps
{
    [Then(@"the properties dialog shows the size, created, modified and accessed dates")]
    public void ThenThePropertiesDialogShowsTheSizeCreatedModifiedAndAccessedDates() => throw new PendingStepException();

    [Given(@"the properties dialog is open for {string}")]
    public void GivenThePropertiesDialogIsOpenForValue(string value1) => throw new PendingStepException();

    [When(@"I set the modified date to {string} and save")]
    public void WhenISetTheModifiedDateToValueAndSave(string value1) => throw new PendingStepException();

    [Then(@"{string} was last modified on 2021-03-04 05:06:07")]
    public void ThenValueWasLastModifiedOn20210304050607(string value1) => throw new PendingStepException();

    [Then(@"the dialog says the date is not valid")]
    public void ThenTheDialogSaysTheDateIsNotValid() => throw new PendingStepException();

    [Then(@"no date of {string} is changed")]
    public void ThenNoDateOfValueIsChanged(string value1) => throw new PendingStepException();

    [Then(@"the dialog shows the folder's size as a running total")]
    public void ThenTheDialogShowsTheFolderSSizeAsARunningTotal() => throw new PendingStepException();

    [Then(@"the dialog describes {string} as a link to {string}")]
    public void ThenTheDialogDescribesValueAsALinkToValue(string value1, string value2) => throw new PendingStepException();

    [Given(@"{string} has the colour {string} and the tag {string}")]
    public void GivenValueHasTheColourValueAndTheTagValue(string value1, string value2, string value3) => throw new PendingStepException();

    [Then(@"the dialog shows the colour {string} and the tag {string}")]
    public void ThenTheDialogShowsTheColourValueAndTheTagValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the permissions dialog for {string} is shown")]
    public void ThenThePermissionsDialogForValueIsShown(string value1) => throw new PendingStepException();
}

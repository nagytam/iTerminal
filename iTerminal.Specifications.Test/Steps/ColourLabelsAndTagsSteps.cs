// Step bindings for Features/ColourLabelsAndTags.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class ColourLabelsAndTagsSteps
{
    [Given(@"{string} and {string} are marked")]
    public void GivenValueAndValueAreMarked(string value1, string value2) => throw new PendingStepException();

    [When(@"I open the Edit menu and choose {string}")]
    public void WhenIOpenTheEditMenuAndChooseValue(string value1) => throw new PendingStepException();

    [When(@"I choose the colour {string}")]
    public void WhenIChooseTheColourValue(string value1) => throw new PendingStepException();

    [Then(@"{string} and {string} are shown in blue")]
    public void ThenValueAndValueAreShownInBlue(string value1, string value2) => throw new PendingStepException();

    [When(@"I type the tags {string}")]
    public void WhenITypeTheTagsValue(string value1) => throw new PendingStepException();

    [Then(@"the file has the tags {string} and {string}")]
    public void ThenTheFileHasTheTagsValueAndValue(string value1, string value2) => throw new PendingStepException();

    [Given(@"{string} has the colour {string} and {string} has no colour")]
    public void GivenValueHasTheColourValueAndValueHasNoColour(string value1, string value2, string value3) => throw new PendingStepException();

    [When(@"I mark both and open {string}")]
    public void WhenIMarkBothAndOpenValue(string value1) => throw new PendingStepException();

    [Then(@"the colour is shown as {string}")]
    public void ThenTheColourIsShownAsValue(string value1) => throw new PendingStepException();

    [Given(@"{string} is red and {string} has no label")]
    public void GivenValueIsRedAndValueHasNoLabel(string value1, string value2) => throw new PendingStepException();

    [Then(@"the left panel lists {string} and its folders")]
    public void ThenTheLeftPanelListsValueAndItsFolders(string value1) => throw new PendingStepException();

    [Then(@"the left panel does not list {string}")]
    public void ThenTheLeftPanelDoesNotListValue(string value1) => throw new PendingStepException();

    [Given(@"{string} is red")]
    public void GivenValueIsRed(string value1) => throw new PendingStepException();

    [When(@"I rename {string} to {string}")]
    public void WhenIRenameValueToValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} has no colour")]
    public void ThenValueHasNoColour(string value1) => throw new PendingStepException();

    [Then(@"the Colour Labels and Tags command is not available")]
    public void ThenTheColourLabelsAndTagsCommandIsNotAvailable() => throw new PendingStepException();
}

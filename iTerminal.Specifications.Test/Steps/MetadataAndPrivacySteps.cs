// Step bindings for Features/MetadataAndPrivacy.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class MetadataAndPrivacySteps
{
    [Given(@"{string} with GPS coordinates is marked")]
    public void GivenValueWithGPSCoordinatesIsMarked(string value1) => throw new PendingStepException();

    [When(@"I press {string} twice")]
    public void WhenIPressValueTwice(string value1) => throw new PendingStepException();

    [Then(@"{string} no longer holds its GPS coordinates")]
    public void ThenValueNoLongerHoldsItsGPSCoordinates(string value1) => throw new PendingStepException();

    [Then(@"its picture and colour profile are unchanged")]
    public void ThenItsPictureAndColourProfileAreUnchanged() => throw new PendingStepException();

    [Then(@"the dialog says the marked files will be overwritten")]
    public void ThenTheDialogSaysTheMarkedFilesWillBeOverwritten() => throw new PendingStepException();

    [Then(@"no file has changed yet")]
    public void ThenNoFileHasChangedYet() => throw new PendingStepException();

    [When(@"I strip the metadata")]
    public void WhenIStripTheMetadata() => throw new PendingStepException();

    [Then(@"{string} is not changed and the summary counts it as skipped")]
    public void ThenValueIsNotChangedAndTheSummaryCountsItAsSkipped(string value1) => throw new PendingStepException();

    [Given(@"{string} has no metadata")]
    public void GivenValueHasNoMetadata(string value1) => throw new PendingStepException();

    [When(@"I strip the metadata of {string}")]
    public void WhenIStripTheMetadataOfValue(string value1) => throw new PendingStepException();

    [Then(@"{string} keeps its modification date")]
    public void ThenValueKeepsItsModificationDate(string value1) => throw new PendingStepException();

    [Given(@"the image viewer shows {string}")]
    public void GivenTheImageViewerShowsValue(string value1) => throw new PendingStepException();

    [Then(@"the GPS position is listed")]
    public void ThenTheGPSPositionIsListed() => throw new PendingStepException();

    [Given(@"the metadata view of {string} is open")]
    public void GivenTheMetadataViewOfValueIsOpen(string value1) => throw new PendingStepException();

    [When(@"I press {string} and change the description to {string}")]
    public void WhenIPressValueAndChangeTheDescriptionToValue(string value1, string value2) => throw new PendingStepException();

    [When(@"I save")]
    public void WhenISave() => throw new PendingStepException();

    [Then(@"the description of {string} is {string}")]
    public void ThenTheDescriptionOfValueIsValue(string value1, string value2) => throw new PendingStepException();
}

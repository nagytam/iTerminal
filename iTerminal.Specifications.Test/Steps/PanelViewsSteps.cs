// Step bindings for Features/PanelViews.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class PanelViewsSteps
{
    [Given(@"the left panel lists many files")]
    public void GivenTheLeftPanelListsManyFiles() => throw new PendingStepException();

    [Then(@"the names are shown in several columns, filled row by row")]
    public void ThenTheNamesAreShownInSeveralColumnsFilledRowByRow() => throw new PendingStepException();

    [Given(@"the left panel is in brief view")]
    public void GivenTheLeftPanelIsInBriefView() => throw new PendingStepException();

    [Then(@"the cursor moves to the next column")]
    public void ThenTheCursorMovesToTheNextColumn() => throw new PendingStepException();

    [Then(@"the left panel shows the folder the right panel showed")]
    public void ThenTheLeftPanelShowsTheFolderTheRightPanelShowed() => throw new PendingStepException();

    [Then(@"the right panel shows the folder the left panel showed")]
    public void ThenTheRightPanelShowsTheFolderTheLeftPanelShowed() => throw new PendingStepException();

    [Given(@"{string} was created in the left folder by another program")]
    public void GivenValueWasCreatedInTheLeftFolderByAnotherProgram(string value1) => throw new PendingStepException();

    [Then(@"the left panel shows the root folder")]
    public void ThenTheLeftPanelShowsTheRootFolder() => throw new PendingStepException();

    [Then(@"the footer shows the size of {string}")]
    public void ThenTheFooterShowsTheSizeOfValue(string value1) => throw new PendingStepException();

    [Given(@"{string} contains a link back to its parent folder")]
    public void GivenValueContainsALinkBackToItsParentFolder(string value1) => throw new PendingStepException();

    [Then(@"the size is counted without following the link forever")]
    public void ThenTheSizeIsCountedWithoutFollowingTheLinkForever() => throw new PendingStepException();

    [Then(@"the footer shows the number of marks and their total size")]
    public void ThenTheFooterShowsTheNumberOfMarksAndTheirTotalSize() => throw new PendingStepException();

    [Then(@"the cursor moves to the first entry whose name starts with {string}")]
    public void ThenTheCursorMovesToTheFirstEntryWhoseNameStartsWithValue(string value1) => throw new PendingStepException();

    [Given(@"the right panel is the preview of {string}")]
    public void GivenTheRightPanelIsThePreviewOfValue(string value1) => throw new PendingStepException();

    [When(@"I press {string} in the right panel")]
    public void WhenIPressValueInTheRightPanel(string value1) => throw new PendingStepException();

    [Then(@"the right panel stays in its normal view")]
    public void ThenTheRightPanelStaysInItsNormalView() => throw new PendingStepException();
}

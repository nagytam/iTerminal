// Step bindings for Features/WorkspacesAndFolderSelector.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class WorkspacesAndFolderSelectorSteps
{
    [Given(@"the left panel shows {string} and the right panel shows {string}")]
    public void GivenTheLeftPanelShowsValueAndTheRightPanelShowsValue(string value1, string value2) => throw new PendingStepException();

    [When(@"I press {string} and save a workspace named {string}")]
    public void WhenIPressValueAndSaveAWorkspaceNamedValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the workspace {string} is listed")]
    public void ThenTheWorkspaceValueIsListed(string value1) => throw new PendingStepException();

    [Given(@"the workspace {string} shows {string} on the right")]
    public void GivenTheWorkspaceValueShowsValueOnTheRight(string value1, string value2) => throw new PendingStepException();

    [When(@"I choose {string} in the workspace manager")]
    public void WhenIChooseValueInTheWorkspaceManager(string value1) => throw new PendingStepException();

    [Given(@"the workspace {string} shows a folder that no longer exists")]
    public void GivenTheWorkspaceValueShowsAFolderThatNoLongerExists(string value1) => throw new PendingStepException();

    [When(@"I switch to {string}")]
    public void WhenISwitchToValue(string value1) => throw new PendingStepException();

    [Then(@"the panel keeps its place and a message says the folder is missing")]
    public void ThenThePanelKeepsItsPlaceAndAMessageSaysTheFolderIsMissing() => throw new PendingStepException();

    [Given(@"two workspaces are saved")]
    public void GivenTwoWorkspacesAreSaved() => throw new PendingStepException();

    [When(@"I press {string} in the workspace manager")]
    public void WhenIPressValueInTheWorkspaceManager(string value1) => throw new PendingStepException();

    [Then(@"the second workspace is shown")]
    public void ThenTheSecondWorkspaceIsShown() => throw new PendingStepException();

    [Then(@"the folder selector is shown with its pages")]
    public void ThenTheFolderSelectorIsShownWithItsPages() => throw new PendingStepException();

    [Given(@"{string} was opened last")]
    public void GivenValueWasOpenedLast(string value1) => throw new PendingStepException();

    [Then(@"{string} is first in the Recent page")]
    public void ThenValueIsFirstInTheRecentPage(string value1) => throw new PendingStepException();

    [Then(@"the panel shows the same folder as before")]
    public void ThenThePanelShowsTheSameFolderAsBefore() => throw new PendingStepException();

    [When(@"I add {string} to the Favourites")]
    public void WhenIAddValueToTheFavourites(string value1) => throw new PendingStepException();

    [Then(@"{string} is listed on the Favourites page")]
    public void ThenValueIsListedOnTheFavouritesPage(string value1) => throw new PendingStepException();

    [When(@"I press {string} and go to the Tree page")]
    public void WhenIPressValueAndGoToTheTreePage(string value1) => throw new PendingStepException();

    [Then(@"the folders are shown as an expandable tree")]
    public void ThenTheFoldersAreShownAsAnExpandableTree() => throw new PendingStepException();
}

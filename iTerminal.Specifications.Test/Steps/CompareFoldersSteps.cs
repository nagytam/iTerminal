// Step bindings for Features/CompareFolders.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class CompareFoldersSteps
{
    [Given(@"the left panel and right panel show folders that differ")]
    public void GivenTheLeftPanelAndRightPanelShowFoldersThatDiffer() => throw new PendingStepException();

    [Then(@"the comparison lists each file that is only on one side")]
    public void ThenTheComparisonListsEachFileThatIsOnlyOnOneSide() => throw new PendingStepException();

    [Then(@"each file that is newer on one side is listed with its direction")]
    public void ThenEachFileThatIsNewerOnOneSideIsListedWithItsDirection() => throw new PendingStepException();

    [Given(@"{string} is newer in the left folder")]
    public void GivenValueIsNewerInTheLeftFolder(string value1) => throw new PendingStepException();

    [When(@"I press {string} and {string}")]
    public void WhenIPressValueAndValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} is marked in the left panel")]
    public void ThenValueIsMarkedInTheLeftPanel(string value1) => throw new PendingStepException();

    [Given(@"{string} has the same date but a different size on both sides")]
    public void GivenValueHasTheSameDateButADifferentSizeOnBothSides(string value1) => throw new PendingStepException();

    [Then(@"{string} is listed as different")]
    public void ThenValueIsListedAsDifferent(string value1) => throw new PendingStepException();

    [Then(@"{string} is not marked on either side")]
    public void ThenValueIsNotMarkedOnEitherSide(string value1) => throw new PendingStepException();

    [Given(@"two files have the same name, size and date but different bytes")]
    public void GivenTwoFilesHaveTheSameNameSizeAndDateButDifferentBytes() => throw new PendingStepException();

    [When(@"I choose {string} and compare")]
    public void WhenIChooseValueAndCompare(string value1) => throw new PendingStepException();

    [Then(@"the files are listed as different")]
    public void ThenTheFilesAreListedAsDifferent() => throw new PendingStepException();

    [When(@"I tick {string} and compare")]
    public void WhenITickValueAndCompare(string value1) => throw new PendingStepException();

    [Then(@"the differences inside the subfolders are listed")]
    public void ThenTheDifferencesInsideTheSubfoldersAreListed() => throw new PendingStepException();

    [Given(@"the comparison lists differences")]
    public void GivenTheComparisonListsDifferences() => throw new PendingStepException();

    [Then(@"a summary of the actions is shown")]
    public void ThenASummaryOfTheActionsIsShown() => throw new PendingStepException();

    [Then(@"nothing is copied yet")]
    public void ThenNothingIsCopiedYet() => throw new PendingStepException();

    [When(@"I press {string} again")]
    public void WhenIPressValueAgain(string value1) => throw new PendingStepException();

    [Then(@"the actions run in a progress window")]
    public void ThenTheActionsRunInAProgressWindow() => throw new PendingStepException();

    [Given(@"{string} exists only in the right folder")]
    public void GivenValueExistsOnlyInTheRightFolder(string value1) => throw new PendingStepException();

    [When(@"I choose {string} with deletions allowed")]
    public void WhenIChooseValueWithDeletionsAllowed(string value1) => throw new PendingStepException();

    [Then(@"the plan lists the deletion of {string}")]
    public void ThenThePlanListsTheDeletionOfValue(string value1) => throw new PendingStepException();

    [Then(@"the deletion needs a second confirmation")]
    public void ThenTheDeletionNeedsASecondConfirmation() => throw new PendingStepException();

    [Given(@"a sync deletes {string}")]
    public void GivenASyncDeletesValue(string value1) => throw new PendingStepException();

    [When(@"the sync runs")]
    public void WhenTheSyncRuns() => throw new PendingStepException();

    [Then(@"{string} is in the recycle bin")]
    public void ThenValueIsInTheRecycleBin(string value1) => throw new PendingStepException();

    [Given(@"{string} differs on both sides with no newer side")]
    public void GivenValueDiffersOnBothSidesWithNoNewerSide(string value1) => throw new PendingStepException();

    [When(@"I synchronise both ways")]
    public void WhenISynchroniseBothWays() => throw new PendingStepException();

    [Then(@"{string} is counted as a conflict and left unchanged")]
    public void ThenValueIsCountedAsAConflictAndLeftUnchanged(string value1) => throw new PendingStepException();

    [Given(@"a comparison of large folders is running")]
    public void GivenAComparisonOfLargeFoldersIsRunning() => throw new PendingStepException();

    [Then(@"the comparison stops")]
    public void ThenTheComparisonStops() => throw new PendingStepException();

    [When(@"I open the Edit menu")]
    public void WhenIOpenTheEditMenu() => throw new PendingStepException();

    [Then(@"the planned actions are listed")]
    public void ThenThePlannedActionsAreListed() => throw new PendingStepException();

    [Then(@"no marks are set in either panel")]
    public void ThenNoMarksAreSetInEitherPanel() => throw new PendingStepException();

    [Given(@"the Synchronise Folders dialog is open")]
    public void GivenTheSynchroniseFoldersDialogIsOpen() => throw new PendingStepException();

    [Then(@"the direction changes to {string}")]
    public void ThenTheDirectionChangesToValue(string value1) => throw new PendingStepException();

    [Then(@"subfolders are included")]
    public void ThenSubfoldersAreIncluded() => throw new PendingStepException();

    [Given(@"a plan is listed")]
    public void GivenAPlanIsListed() => throw new PendingStepException();

    [Then(@"the listed plan is cleared")]
    public void ThenTheListedPlanIsCleared() => throw new PendingStepException();
}

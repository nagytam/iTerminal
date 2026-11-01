// Step bindings for Features/GitIntegration.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class GitIntegrationSteps
{
    [Given(@"the left panel is inside a git repository on branch {string}")]
    public void GivenTheLeftPanelIsInsideAGitRepositoryOnBranchValue(string value1) => throw new PendingStepException();

    [Then(@"the focused panel lists the commits of the repository")]
    public void ThenTheFocusedPanelListsTheCommitsOfTheRepository() => throw new PendingStepException();

    [Given(@"the history lists commit {string}")]
    public void GivenTheHistoryListsCommitValue(string value1) => throw new PendingStepException();

    [Then(@"the panel shows the files at that commit")]
    public void ThenThePanelShowsTheFilesAtThatCommit() => throw new PendingStepException();

    [Then(@"nothing in the working tree is changed")]
    public void ThenNothingInTheWorkingTreeIsChanged() => throw new PendingStepException();

    [Given(@"the branch {string} is one commit behind its upstream and two commits ahead")]
    public void GivenTheBranchValueIsOneCommitBehindItsUpstreamAndTwoCommitsAhead(string value1) => throw new PendingStepException();

    [Then(@"the panel header shows {string}")]
    public void ThenThePanelHeaderShowsValue(string value1) => throw new PendingStepException();

    [When(@"I open the context menu on a file in the repository")]
    public void WhenIOpenTheContextMenuOnAFileInTheRepository() => throw new PendingStepException();

    [Then(@"git pull --rebase runs in the repository root")]
    public void ThenGitPullRebaseRunsInTheRepositoryRoot() => throw new PendingStepException();

    [Then(@"a notification shows the outcome")]
    public void ThenANotificationShowsTheOutcome() => throw new PendingStepException();

    [Given(@"a rebase would conflict")]
    public void GivenARebaseWouldConflict() => throw new PendingStepException();

    [Then(@"a notification says the rebase stopped on a conflict")]
    public void ThenANotificationSaysTheRebaseStoppedOnAConflict() => throw new PendingStepException();

    [Then(@"the pull runs first")]
    public void ThenThePullRunsFirst() => throw new PendingStepException();

    [Then(@"the push runs only if the pull succeeded")]
    public void ThenThePushRunsOnlyIfThePullSucceeded() => throw new PendingStepException();

    [Given(@"the branch {string} has no upstream")]
    public void GivenTheBranchValueHasNoUpstream(string value1) => throw new PendingStepException();

    [Then(@"a warning says there is no upstream and nothing runs")]
    public void ThenAWarningSaysThereIsNoUpstreamAndNothingRuns() => throw new PendingStepException();

    [Then(@"the Git client is shown for the repository")]
    public void ThenTheGitClientIsShownForTheRepository() => throw new PendingStepException();

    [Given(@"the Git client shows a modified file {string}")]
    public void GivenTheGitClientShowsAModifiedFileValue(string value1) => throw new PendingStepException();

    [When(@"I stage {string} and commit with the message {string}")]
    public void WhenIStageValueAndCommitWithTheMessageValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the commit {string} is on the branch")]
    public void ThenTheCommitValueIsOnTheBranch(string value1) => throw new PendingStepException();

    [Given(@"the Git client is open")]
    public void GivenTheGitClientIsOpen() => throw new PendingStepException();

    [When(@"I start a fetch")]
    public void WhenIStartAFetch() => throw new PendingStepException();

    [Then(@"the screen keeps responding while the fetch runs")]
    public void ThenTheScreenKeepsRespondingWhileTheFetchRuns() => throw new PendingStepException();

    [Given(@"a git command fails")]
    public void GivenAGitCommandFails() => throw new PendingStepException();

    [When(@"I open the Git client")]
    public void WhenIOpenTheGitClient() => throw new PendingStepException();

    [Then(@"the detail pane shows the whole output of the failed command")]
    public void ThenTheDetailPaneShowsTheWholeOutputOfTheFailedCommand() => throw new PendingStepException();
}

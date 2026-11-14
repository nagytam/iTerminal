// Step bindings for Features/Network/Network.YouTube.feature. Each body is pending
// until the step is implemented; the scenarios of that feature are tagged @ignore until then.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps.Network;

[Binding]
public sealed class YouTubeSteps
{
    [Given(@"the YouTube dialog is open")]
    public void TheYouTubeDialogIsOpen() => throw new PendingStepException();

    [When(@"I type {string} in the search and press {string}")]
    public void ITypeValueInTheSearchAndPressValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the videos found for {string} are listed on the left")]
    public void TheVideosFoundForValueAreListedOnTheLeft(string value1) => throw new PendingStepException();

    [Given(@"the YouTube dialog shows the results of a search")]
    public void TheYouTubeDialogShowsTheResultsOfASearch() => throw new PendingStepException();

    [When(@"I move the cursor to the second video")]
    public void IMoveTheCursorToTheSecondVideo() => throw new PendingStepException();

    [Then(@"the details of the second video are shown on the right")]
    public void TheDetailsOfTheSecondVideoAreShownOnTheRight() => throw new PendingStepException();

    [When(@"I move the cursor to the first video and press {string}")]
    public void IMoveTheCursorToTheFirstVideoAndPressValue(string value1) => throw new PendingStepException();

    [Then(@"the video viewer plays the first video")]
    public void TheVideoViewerPlaysTheFirstVideo() => throw new PendingStepException();

    [Then(@"the browser opens the page of the first video")]
    public void TheBrowserOpensThePageOfTheFirstVideo() => throw new PendingStepException();

    [Then(@"the dialog says that no video was found")]
    public void TheDialogSaysThatNoVideoWasFound() => throw new PendingStepException();

    [Given(@"the YouTube dialog is searching")]
    public void TheYouTubeDialogIsSearching() => throw new PendingStepException();

    [Then(@"the search is cancelled")]
    public void TheSearchIsCancelled() => throw new PendingStepException();

    [Then(@"its results never appear")]
    public void ItsResultsNeverAppear() => throw new PendingStepException();
}

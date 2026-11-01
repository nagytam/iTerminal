// Step bindings for Features/MusicPlayer.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class MusicPlayerSteps
{
    [Given(@"the music library holds {string} and {string}")]
    public void GivenTheMusicLibraryHoldsValueAndValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the library is shown in one panel and the queue in the other")]
    public void ThenTheLibraryIsShownInOnePanelAndTheQueueInTheOther() => throw new PendingStepException();

    [Given(@"the library panel lists {string}")]
    public void GivenTheLibraryPanelListsValue(string value1) => throw new PendingStepException();

    [Then(@"{string} starts playing")]
    public void ThenValueStartsPlaying(string value1) => throw new PendingStepException();

    [Given(@"{string} is playing")]
    public void GivenValueIsPlaying(string value1) => throw new PendingStepException();

    [Then(@"playback is paused")]
    public void ThenPlaybackIsPaused() => throw new PendingStepException();

    [Then(@"playback resumes")]
    public void ThenPlaybackResumes() => throw new PendingStepException();

    [Then(@"both tracks are added to the queue")]
    public void ThenBothTracksAreAddedToTheQueue() => throw new PendingStepException();

    [Given(@"the queue holds {string}")]
    public void GivenTheQueueHoldsValue(string value1) => throw new PendingStepException();

    [When(@"I press {string} on {string} in the queue")]
    public void WhenIPressValueOnValueInTheQueue(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} is no longer in the queue")]
    public void ThenValueIsNoLongerInTheQueue(string value1) => throw new PendingStepException();

    [Given(@"the queue holds {string} and {string}")]
    public void GivenTheQueueHoldsValueAndValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the queue still holds both tracks")]
    public void ThenTheQueueStillHoldsBothTracks() => throw new PendingStepException();

    [Then(@"the track that was cued is selected but not playing")]
    public void ThenTheTrackThatWasCuedIsSelectedButNotPlaying() => throw new PendingStepException();

    [Then(@"the volume goes up")]
    public void ThenTheVolumeGoesUp() => throw new PendingStepException();

    [Then(@"the volume goes down")]
    public void ThenTheVolumeGoesDown() => throw new PendingStepException();

    [When(@"I search the library for {string}")]
    public void WhenISearchTheLibraryForValue(string value1) => throw new PendingStepException();

    [When(@"I rescan the music folder")]
    public void WhenIRescanTheMusicFolder() => throw new PendingStepException();

    [Then(@"the scan shows folder discovery first and then indexing with a count")]
    public void ThenTheScanShowsFolderDiscoveryFirstAndThenIndexingWithACount() => throw new PendingStepException();

    [Given(@"a scan of a large music folder is running")]
    public void GivenAScanOfALargeMusicFolderIsRunning() => throw new PendingStepException();

    [Then(@"the library lists the tracks found so far")]
    public void ThenTheLibraryListsTheTracksFoundSoFar() => throw new PendingStepException();

    [When(@"I save the queue as a playlist {string}")]
    public void WhenISaveTheQueueAsAPlaylistValue(string value1) => throw new PendingStepException();

    [Then(@"{string} lists {string}")]
    public void ThenValueListsValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the file panels are shown again")]
    public void ThenTheFilePanelsAreShownAgain() => throw new PendingStepException();

    [Given(@"{string} is under the cursor in a file panel")]
    public void GivenValueIsUnderTheCursorInAFilePanel(string value1) => throw new PendingStepException();

    [Then(@"the file plays in the music player")]
    public void ThenTheFilePlaysInTheMusicPlayer() => throw new PendingStepException();

    [Given(@"an audio output device {string} is selected in the settings")]
    public void GivenAnAudioOutputDeviceValueIsSelectedInTheSettings(string value1) => throw new PendingStepException();

    [When(@"a track plays")]
    public void WhenATrackPlays() => throw new PendingStepException();

    [Then(@"the track is sent to {string}")]
    public void ThenTheTrackIsSentToValue(string value1) => throw new PendingStepException();
}

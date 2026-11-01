// Step bindings for Features/Torrents.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class TorrentsSteps
{
    [Then(@"the torrent manager lists the torrents it knows")]
    public void ThenTheTorrentManagerListsTheTorrentsItKnows() => throw new PendingStepException();

    [When(@"I press {string} and add the magnet link {string}")]
    public void WhenIPressValueAndAddTheMagnetLinkValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"the torrent is listed")]
    public void ThenTheTorrentIsListed() => throw new PendingStepException();

    [When(@"I add the file {string}")]
    public void WhenIAddTheFileValue(string value1) => throw new PendingStepException();

    [Then(@"the file is copied to the torrents folder of the data directory")]
    public void ThenTheFileIsCopiedToTheTorrentsFolderOfTheDataDirectory() => throw new PendingStepException();

    [Given(@"a torrent is downloading")]
    public void GivenATorrentIsDownloading() => throw new PendingStepException();

    [When(@"I pause it")]
    public void WhenIPauseIt() => throw new PendingStepException();

    [Then(@"it stops transferring")]
    public void ThenItStopsTransferring() => throw new PendingStepException();

    [When(@"I resume it")]
    public void WhenIResumeIt() => throw new PendingStepException();

    [Then(@"it downloads again")]
    public void ThenItDownloadsAgain() => throw new PendingStepException();

    [Given(@"a torrent is listed")]
    public void GivenATorrentIsListed() => throw new PendingStepException();

    [When(@"I remove it")]
    public void WhenIRemoveIt() => throw new PendingStepException();

    [Then(@"it is no longer listed")]
    public void ThenItIsNoLongerListed() => throw new PendingStepException();

    [Then(@"the torrent is listed again")]
    public void ThenTheTorrentIsListedAgain() => throw new PendingStepException();
}

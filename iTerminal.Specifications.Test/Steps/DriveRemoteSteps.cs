// Step bindings for Features/DriveRemote.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class DriveRemoteSteps
{
    [When(@"I type {string} on the Path page")]
    public void WhenITypeValueOnThePathPage(string value1) => throw new PendingStepException();

    [Then(@"the focused panel shows {string}")]
    public void ThenTheFocusedPanelShowsValue(string value1) => throw new PendingStepException();

    [Then(@"the focused panel lists the share's folders")]
    public void ThenTheFocusedPanelListsTheShareSFolders() => throw new PendingStepException();

    [Then(@"the password is not saved in the address history")]
    public void ThenThePasswordIsNotSavedInTheAddressHistory() => throw new PendingStepException();

    [Given(@"the left panel shows {string}")]
    public void GivenTheLeftPanelShowsValue(string value1) => throw new PendingStepException();

    [Given(@"the right panel shows a local folder")]
    public void GivenTheRightPanelShowsALocalFolder() => throw new PendingStepException();

    [Then(@"{string} is in the local folder")]
    public void ThenValueIsInTheLocalFolder(string value1) => throw new PendingStepException();

    [When(@"I connect to a host whose key is not known")]
    public void WhenIConnectToAHostWhoseKeyIsNotKnown() => throw new PendingStepException();

    [Then(@"a prompt shows the host's key fingerprint before anything is trusted")]
    public void ThenAPromptShowsTheHostSKeyFingerprintBeforeAnythingIsTrusted() => throw new PendingStepException();

    [Given(@"the saved key for {string} differs from the key the server now shows")]
    public void GivenTheSavedKeyForValueDiffersFromTheKeyTheServerNowShows(string value1) => throw new PendingStepException();

    [When(@"I connect to {string}")]
    public void WhenIConnectToValue(string value1) => throw new PendingStepException();

    [Then(@"the connection is refused with a warning about the changed key")]
    public void ThenTheConnectionIsRefusedWithAWarningAboutTheChangedKey() => throw new PendingStepException();

    [Then(@"the focused panel lists the FTP server's root folder")]
    public void ThenTheFocusedPanelListsTheFTPServerSRootFolder() => throw new PendingStepException();

    [Then(@"the focused panel lists the WebDAV folder")]
    public void ThenTheFocusedPanelListsTheWebDAVFolder() => throw new PendingStepException();

    [Given(@"an Android phone is connected over MTP")]
    public void GivenAnAndroidPhoneIsConnectedOverMTP() => throw new PendingStepException();

    [When(@"I open the drive list")]
    public void WhenIOpenTheDriveList() => throw new PendingStepException();

    [Then(@"the phone is listed as {string}")]
    public void ThenThePhoneIsListedAsValue(string value1) => throw new PendingStepException();

    [Then(@"the panel stays where it was and a message says the host cannot be reached")]
    public void ThenThePanelStaysWhereItWasAndAMessageSaysTheHostCannotBeReached() => throw new PendingStepException();
}

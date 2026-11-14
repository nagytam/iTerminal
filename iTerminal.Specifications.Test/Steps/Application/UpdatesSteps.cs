// Step bindings for Features/Application/Application.Updates.feature. Each body is pending
// until the step is implemented; the scenarios of that feature are tagged @ignore until then.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps.Application;

[Binding]
public sealed class UpdatesSteps
{
    [Given(@"release 2.0.0 is newer than this build")]
    public void Release200IsNewerThanThisBuild() => throw new PendingStepException();

    [When(@"the update check has run")]
    public void TheUpdateCheckHasRun() => throw new PendingStepException();

    [Then(@"the update dialog is shown with {string}")]
    public void TheUpdateDialogIsShownWithValue(string value1) => throw new PendingStepException();

    [Given(@"no release newer than this build exists")]
    public void NoReleaseNewerThanThisBuildExists() => throw new PendingStepException();

    [Then(@"no update dialog is shown")]
    public void NoUpdateDialogIsShown() => throw new PendingStepException();

    [Given(@"release 2.0.0 was already offered in this session")]
    public void Release200WasAlreadyOfferedInThisSession() => throw new PendingStepException();

    [When(@"the update check runs again")]
    public void TheUpdateCheckRunsAgain() => throw new PendingStepException();

    [Then(@"a notification says that release 2.0.0 is available")]
    public void ANotificationSaysThatRelease200IsAvailable() => throw new PendingStepException();

    [Then(@"no update dialog is opened")]
    public void NoUpdateDialogIsOpened() => throw new PendingStepException();

    [Given(@"the update dialog is open for {string}")]
    public void TheUpdateDialogIsOpenForValue(string value1) => throw new PendingStepException();

    [Then(@"the update dialog is closed")]
    public void TheUpdateDialogIsClosed() => throw new PendingStepException();

    [Then(@"release 2.0.0 is not offered again in this session")]
    public void Release200IsNotOfferedAgainInThisSession() => throw new PendingStepException();

    [Then(@"the installer for {string} is downloaded")]
    public void TheInstallerForValueIsDownloaded(string value1) => throw new PendingStepException();

    [Then(@"iTerminal is closed so the installer can replace its files")]
    public void ITerminalIsClosedSoTheInstallerCanReplaceItsFiles() => throw new PendingStepException();

    [Given(@"the app runs on Linux")]
    public void TheAppRunsOnLinux() => throw new PendingStepException();

    [Then(@"the dialog shows the address of release {string}")]
    public void TheDialogShowsTheAddressOfReleaseValue(string value1) => throw new PendingStepException();

    [Then(@"the dialog does not offer to install it")]
    public void TheDialogDoesNotOfferToInstallIt() => throw new PendingStepException();
}

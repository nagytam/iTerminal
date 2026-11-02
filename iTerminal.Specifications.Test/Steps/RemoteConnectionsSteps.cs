// Step bindings for Features/RemoteConnections.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class RemoteConnectionsSteps
{
    [Given(@"two connections are saved")]
    public void GivenTwoConnectionsAreSaved() => throw new PendingStepException();

    [Then(@"both connections are listed")]
    public void ThenBothConnectionsAreListed() => throw new PendingStepException();

    [When(@"I press {string} and add an SSH connection to {string} as {string}")]
    public void WhenIPressValueAndAddAnSSHConnectionToValueAsValue(string value1, string value2, string value3) => throw new PendingStepException();

    [Given(@"an SSH connection {string} is saved")]
    public void GivenAnSSHConnectionValueIsSaved(string value1) => throw new PendingStepException();

    [When(@"I press {string} on it")]
    public void WhenIPressValueOnIt(string value1) => throw new PendingStepException();

    [Then(@"the terminal is lent to an ssh session for that host")]
    public void ThenTheTerminalIsLentToAnSshSessionForThatHost() => throw new PendingStepException();

    [Given(@"an SSH connection is saved")]
    public void GivenAnSSHConnectionIsSaved() => throw new PendingStepException();

    [Then(@"the focused panel shows the ssh:\/\/ drive for that host")]
    public void ThenTheFocusedPanelShowsTheSshDriveForThatHost() => throw new PendingStepException();

    [When(@"I add an SSH connection with a password")]
    public void WhenIAddAnSSHConnectionWithAPassword() => throw new PendingStepException();

    [Then(@"the connection is saved without the password")]
    public void ThenTheConnectionIsSavedWithoutThePassword() => throw new PendingStepException();

    [Then(@"the session asks for the password on the terminal")]
    public void ThenTheSessionAsksForThePasswordOnTheTerminal() => throw new PendingStepException();

    [Given(@"an FTP connection with a stored password is saved")]
    public void GivenAnFTPConnectionWithAStoredPasswordIsSaved() => throw new PendingStepException();

    [Then(@"the focused panel lists the FTP folder")]
    public void ThenTheFocusedPanelListsTheFTPFolder() => throw new PendingStepException();

    [Given(@"a PowerShell remoting connection is saved")]
    public void GivenAPowerShellRemotingConnectionIsSaved() => throw new PendingStepException();

    [Then(@"a PowerShell remoting session is started on the lent terminal")]
    public void ThenAPowerShellRemotingSessionIsStartedOnTheLentTerminal() => throw new PendingStepException();

    [Given(@"a connection is saved")]
    public void GivenAConnectionIsSaved() => throw new PendingStepException();

    [Then(@"the dialog asks whether to delete it")]
    public void ThenTheDialogAsksWhetherToDeleteIt() => throw new PendingStepException();

    [When(@"I confirm")]
    public void WhenIConfirm() => throw new PendingStepException();

    [Then(@"the connection is no longer listed")]
    public void ThenTheConnectionIsNoLongerListed() => throw new PendingStepException();
}

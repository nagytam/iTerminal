// Step bindings for Features/DockerContainers.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class DockerContainersSteps
{
    [Given(@"Docker is running with a container {string} that is stopped")]
    public void GivenDockerIsRunningWithAContainerValueThatIsStopped(string value1) => throw new PendingStepException();

    [Then(@"the focused panel lists {string}")]
    public void ThenTheFocusedPanelListsValue(string value1) => throw new PendingStepException();

    [Then(@"the panel shows the files inside the container")]
    public void ThenThePanelShowsTheFilesInsideTheContainer() => throw new PendingStepException();

    [Then(@"the container {string} is started")]
    public void ThenTheContainerValueIsStarted(string value1) => throw new PendingStepException();

    [Given(@"the container {string} is running")]
    public void GivenTheContainerValueIsRunning(string value1) => throw new PendingStepException();

    [Then(@"the container {string} is stopped")]
    public void ThenTheContainerValueIsStopped(string value1) => throw new PendingStepException();

    [Then(@"the container {string} is restarted")]
    public void ThenTheContainerValueIsRestarted(string value1) => throw new PendingStepException();

    [Given(@"Docker is not installed")]
    public void GivenDockerIsNotInstalled() => throw new PendingStepException();

    [Then(@"the panel says that Docker is not available")]
    public void ThenThePanelSaysThatDockerIsNotAvailable() => throw new PendingStepException();

    [Given(@"the left panel shows a folder inside container {string}")]
    public void GivenTheLeftPanelShowsAFolderInsideContainerValue(string value1) => throw new PendingStepException();

    [Then(@"the file is in the right panel's folder")]
    public void ThenTheFileIsInTheRightPanelSFolder() => throw new PendingStepException();
}

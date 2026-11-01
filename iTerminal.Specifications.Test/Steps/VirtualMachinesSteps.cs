// Step bindings for Features/VirtualMachines.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class VirtualMachinesSteps
{
    [Given(@"libvirt has a machine {string} that is shut off")]
    public void GivenLibvirtHasAMachineValueThatIsShutOff(string value1) => throw new PendingStepException();

    [Then(@"{string} is listed as {string}")]
    public void ThenValueIsListedAsValue(string value1, string value2) => throw new PendingStepException();

    [Then(@"{string} is started")]
    public void ThenValueIsStarted(string value1) => throw new PendingStepException();

    [When(@"I press {string} on a running machine")]
    public void WhenIPressValueOnARunningMachine(string value1) => throw new PendingStepException();

    [Then(@"nothing is changed")]
    public void ThenNothingIsChanged() => throw new PendingStepException();

    [Given(@"{string} is running")]
    public void GivenValueIsRunning(string value1) => throw new PendingStepException();

    [Then(@"the status line asks whether to shut down {string}")]
    public void ThenTheStatusLineAsksWhetherToShutDownValue(string value1) => throw new PendingStepException();

    [Then(@"{string} shuts down")]
    public void ThenValueShutsDown(string value1) => throw new PendingStepException();

    [Then(@"{string} is powered off")]
    public void ThenValueIsPoweredOff(string value1) => throw new PendingStepException();

    [Then(@"the console opens in virt-viewer")]
    public void ThenTheConsoleOpensInVirtViewer() => throw new PendingStepException();

    [Given(@"the user is not in the libvirt group")]
    public void GivenTheUserIsNotInTheLibvirtGroup() => throw new PendingStepException();

    [Then(@"the message names the libvirt group")]
    public void ThenTheMessageNamesTheLibvirtGroup() => throw new PendingStepException();

    [Then(@"the Tools menu does not list {string}")]
    public void ThenTheToolsMenuDoesNotListValue(string value1) => throw new PendingStepException();
}

// Step bindings for Features/Bluetooth.feature. Each body is pending until the step is implemented;
// the scenarios of that feature are tagged @ignore until the bindings are implemented and tested.
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

[Binding]
public sealed class BluetoothSteps
{
    [Given(@"the machine runs Linux with BlueZ")]
    public void GivenTheMachineRunsLinuxWithBlueZ() => throw new PendingStepException();

    [Then(@"the paired and recently seen devices are listed with connected, paired or nearby state")]
    public void ThenThePairedAndRecentlySeenDevicesAreListedWithConnectedPairedOrNearbyState() => throw new PendingStepException();

    [Then(@"the scan runs for eight seconds and then the list shows the devices found")]
    public void ThenTheScanRunsForEightSecondsAndThenTheListShowsTheDevicesFound() => throw new PendingStepException();

    [Given(@"a paired headset is under the cursor and not connected")]
    public void GivenAPairedHeadsetIsUnderTheCursorAndNotConnected() => throw new PendingStepException();

    [Then(@"the headset is connected")]
    public void ThenTheHeadsetIsConnected() => throw new PendingStepException();

    [Then(@"the status line says it is connected")]
    public void ThenTheStatusLineSaysItIsConnected() => throw new PendingStepException();

    [Given(@"a connected headset is under the cursor")]
    public void GivenAConnectedHeadsetIsUnderTheCursor() => throw new PendingStepException();

    [Then(@"the headset is disconnected")]
    public void ThenTheHeadsetIsDisconnected() => throw new PendingStepException();

    [Given(@"a connection is being made")]
    public void GivenAConnectionIsBeingMade() => throw new PendingStepException();

    [When(@"I press {string} on another device")]
    public void WhenIPressValueOnAnotherDevice(string value1) => throw new PendingStepException();

    [Then(@"nothing starts and the status line says an operation is already running")]
    public void ThenNothingStartsAndTheStatusLineSaysAnOperationIsAlreadyRunning() => throw new PendingStepException();

    [Given(@"the machine runs Windows")]
    public void GivenTheMachineRunsWindows() => throw new PendingStepException();

    [When(@"I open the Tools menu")]
    public void WhenIOpenTheToolsMenu() => throw new PendingStepException();

    [Then(@"{string} is not listed")]
    public void ThenValueIsNotListed(string value1) => throw new PendingStepException();
}

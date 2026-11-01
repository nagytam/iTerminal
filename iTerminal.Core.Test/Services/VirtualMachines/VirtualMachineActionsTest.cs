using iTerminal.Core.Services.VirtualMachines;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.VirtualMachines.Test;

[TestClass]
public class VirtualMachineActionsTest
{
    private static string Offered(VirtualMachineState state)
    {
        var machine = new VirtualMachine("vm", state, state.ToString());
        return string.Join(",", Enum.GetValues<VirtualMachineAction>()
            .Where(action => VirtualMachineActions.IsAvailable(machine, action)));
    }

    // Reverted (an action is offered in a state it must not be in): the matching test fails.

    [TestMethod]
    public void AShutOffMachineOnlyStarts() =>
        Assert.AreEqual("Start", Offered(VirtualMachineState.ShutOff));

    [TestMethod]
    public void ARunningGuestCanBeShutDownRestartedPausedSavedAndOpened() =>
        Assert.AreEqual("Shutdown,PowerOff,Restart,Pause,SaveState,Console", Offered(VirtualMachineState.Running));

    [TestMethod]
    public void AnIdleGuestOffersTheSameActionsAsARunningOne() =>
        Assert.AreEqual(Offered(VirtualMachineState.Running), Offered(VirtualMachineState.Idle));

    [TestMethod]
    public void APausedMachineCanBeResumedButNotPausedAgain() =>
        Assert.AreEqual("PowerOff,Resume,SaveState,Console", Offered(VirtualMachineState.Paused));

    [TestMethod]
    public void AnUnknownStateOffersNothing() =>
        Assert.AreEqual(string.Empty, Offered(VirtualMachineState.Unknown));

    [TestMethod]
    public void StopsAndRestartsAskFirstAndTheRestActOnTheSpot()
    {
        Assert.IsTrue(VirtualMachineActions.NeedsConfirmation(VirtualMachineAction.Shutdown));
        Assert.IsTrue(VirtualMachineActions.NeedsConfirmation(VirtualMachineAction.PowerOff));
        Assert.IsTrue(VirtualMachineActions.NeedsConfirmation(VirtualMachineAction.Restart));
        Assert.IsTrue(VirtualMachineActions.NeedsConfirmation(VirtualMachineAction.SaveState));
        Assert.IsFalse(VirtualMachineActions.NeedsConfirmation(VirtualMachineAction.Start));
        Assert.IsFalse(VirtualMachineActions.NeedsConfirmation(VirtualMachineAction.Pause));
        Assert.IsFalse(VirtualMachineActions.NeedsConfirmation(VirtualMachineAction.Resume));
        Assert.IsFalse(VirtualMachineActions.NeedsConfirmation(VirtualMachineAction.Console));
    }

    [TestMethod]
    [DataRow(VirtualMachineAction.Start, "start")]
    [DataRow(VirtualMachineAction.Shutdown, "shutdown")]
    [DataRow(VirtualMachineAction.PowerOff, "destroy")]
    [DataRow(VirtualMachineAction.Restart, "reboot")]
    [DataRow(VirtualMachineAction.Pause, "suspend")]
    [DataRow(VirtualMachineAction.Resume, "resume")]
    [DataRow(VirtualMachineAction.SaveState, "managedsave")]
    public void EachActionIsTheVirshCommandLibvirtNamesIt(VirtualMachineAction action, string verb) =>
        Assert.AreEqual(verb, VirtualMachineActions.Verb(action));
}

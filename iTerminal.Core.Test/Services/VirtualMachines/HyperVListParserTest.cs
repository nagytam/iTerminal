using iTerminal.Core.Services.VirtualMachines;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.VirtualMachines.Test;

[TestClass]
public class HyperVListParserTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void AnArrayGivesOneMachineEachInTheOrderPrinted()
    {
        var machines = HyperVListParser.Parse("""[{"Name":"web","State":"Running"},{"Name":"db","State":"Paused"}]""");

        Assert.HasCount(2, machines);
        Assert.AreEqual(new VirtualMachine("web", VirtualMachineState.Running, "Running"), machines[0]);
        Assert.AreEqual(new VirtualMachine("db", VirtualMachineState.Paused, "Paused"), machines[1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ASingleObjectIsOneMachine()
    {
        var machine = Assert.ContainsSingle(HyperVListParser.Parse("""{"Name":"web","State":"Off"}"""));

        Assert.AreEqual(VirtualMachineState.ShutOff, machine.State);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ANameWithSpacesSurvives()
    {
        var machine = Assert.ContainsSingle(HyperVListParser.Parse("""[{"Name":"Windows 11 dev","State":"Running"}]"""));

        Assert.AreEqual("Windows 11 dev", machine.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("[]")]
    [DataRow("not json at all")]
    [DataRow("42")]
    [DataRow("""[{"State":"Running"}]""")]
    public void NothingUsableGivesAnEmptyList(string json)
    {
        Assert.IsEmpty(HyperVListParser.Parse(json));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("Running", VirtualMachineState.Running)]
    [DataRow("Off", VirtualMachineState.ShutOff)]
    [DataRow("Saved", VirtualMachineState.ShutOff)]
    [DataRow("Paused", VirtualMachineState.Paused)]
    [DataRow("Stopping", VirtualMachineState.InShutdown)]
    [DataRow("Saving", VirtualMachineState.InShutdown)]
    [DataRow("RunningCritical", VirtualMachineState.Crashed)]
    [DataRow("PausedCritical", VirtualMachineState.Crashed)]
    [DataRow("Starting", VirtualMachineState.Unknown)]
    [DataRow("Resuming", VirtualMachineState.Unknown)]
    [DataRow("something new", VirtualMachineState.Unknown)]
    public void EachHyperVWordMapsToTheStateThatDecidesTheActions(string word, VirtualMachineState expected)
    {
        Assert.AreEqual(expected, HyperVListParser.StateOf(word));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ASavedMachineCanBeStartedButNotShutDown()
    {
        var machine = Assert.ContainsSingle(HyperVListParser.Parse("""[{"Name":"web","State":"Saved"}]"""));

        Assert.IsTrue(VirtualMachineActions.IsAvailable(machine, VirtualMachineAction.Start));
        Assert.IsFalse(VirtualMachineActions.IsAvailable(machine, VirtualMachineAction.Shutdown));
        Assert.AreEqual("Saved", machine.StateText);
    }
}

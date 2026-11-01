using iTerminal.Core.Services.VirtualMachines;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.VirtualMachines.Test;

[TestClass]
public class VirshListParserTest
{
    /// <summary>What <c>virsh -c qemu:///system list --all</c> printed on the development machine, with no domains defined.</summary>
    private const string EmptyListCapturedFromLibvirt = " Id   Name   State\n--------------------\n\n";

    [TestMethod]
    public void ParsesEmptyListCapturedFromLibvirt()
    {
        // Reverted (the parser returns nothing, or throws on the header): this fails.
        CollectionAssert.AreEqual(Array.Empty<VirtualMachine>(), VirshListParser.Parse(EmptyListCapturedFromLibvirt).ToArray());
    }

    // The fixtures below follow libvirt's documented table layout. They are not captured output yet:
    // they stay _Unverified until a real domain can be listed on a machine.

    [TestMethod]
    public void ParsesRunningPausedAndShutOffRows_Unverified()
    {
        const string output =
            " Id   Name     State\n" +
            "----------------------\n" +
            " 1    web      running\n" +
            " 2    db       paused\n" +
            " -    backup   shut off\n";

        var machines = VirshListParser.Parse(output);

        Assert.AreEqual(3, machines.Count);
        Assert.AreEqual(new VirtualMachine("web", VirtualMachineState.Running, "running"), machines[0]);
        Assert.AreEqual(new VirtualMachine("db", VirtualMachineState.Paused, "paused"), machines[1]);
        Assert.AreEqual(new VirtualMachine("backup", VirtualMachineState.ShutOff, "shut off"), machines[2]);
    }

    [TestMethod]
    public void ReadsAStateWordsAndANameWithSpaces_Unverified()
    {
        const string output =
            " Id   Name         State\n" +
            "-----------------------------\n" +
            " 3    my test vm   in shutdown\n";

        var machine = Assert.ContainsSingle(VirshListParser.Parse(output));

        Assert.AreEqual("my test vm", machine.Name);
        Assert.AreEqual(VirtualMachineState.InShutdown, machine.State);
    }

    [TestMethod]
    public void AStateNobodyKnowsIsKeptAsUnknownAndNotDropped_Unverified()
    {
        const string output = " -    odd    frozen solid\n";

        var machine = Assert.ContainsSingle(VirshListParser.Parse(output));

        Assert.AreEqual("odd    frozen solid", machine.Name);
        Assert.AreEqual(VirtualMachineState.Unknown, machine.State);
    }
}

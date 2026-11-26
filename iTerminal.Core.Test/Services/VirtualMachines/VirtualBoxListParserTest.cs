using iTerminal.Core.Services.VirtualMachines;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.VirtualMachines.Test;

[TestClass]
public class VirtualBoxListParserTest
{
    private const string Sample = """
        Name:            Ubuntu 24.04
        Groups:          /
        Guest OS:        Ubuntu (64-bit)
        UUID:            6f1c2b3a-0000-4000-8000-000000000001
        State:           running (since 2026-11-01T10:00:00.000000000)

        Name:            Windows 11
        Groups:          /
        UUID:            6f1c2b3a-0000-4000-8000-000000000002
        State:           powered off (since 2026-11-02T09:00:00.000000000)

        Name:            Paused box
        State:           paused (since 2026-11-03T09:00:00.000000000)

        Name:            Saved box
        State:           saved (since 2026-11-04T09:00:00.000000000)

        Name:            Crashed box
        State:           aborted (since 2026-11-05T09:00:00.000000000)

        Name:            Mystery
        State:           starting (since 2026-11-06T09:00:00.000000000)
        """;

    [Timeout(60_000)]
    [TestMethod]
    public void ReadsEachMachineNameAndItsStateWords()
    {
        var machines = VirtualBoxListParser.Parse(Sample);

        CollectionAssert.AreEqual(
            new[]
            {
                new VirtualMachine("Ubuntu 24.04", VirtualMachineState.Running, "running"),
                new VirtualMachine("Windows 11", VirtualMachineState.ShutOff, "powered off"),
                new VirtualMachine("Paused box", VirtualMachineState.Paused, "paused"),
                new VirtualMachine("Saved box", VirtualMachineState.ShutOff, "saved"),
                new VirtualMachine("Crashed box", VirtualMachineState.Crashed, "aborted"),
                new VirtualMachine("Mystery", VirtualMachineState.Unknown, "starting"),
            },
            machines.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AMachineWithoutAStateLineIsUnknownNotDropped()
    {
        var machines = VirtualBoxListParser.Parse("Name:            Bare\nGroups:          /\n");

        var machine = Assert.ContainsSingle(machines);
        Assert.AreEqual("Bare", machine.Name);
        Assert.AreEqual(VirtualMachineState.Unknown, machine.State);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnEmptyListingHasNoMachines()
    {
        Assert.AreEqual(0, VirtualBoxListParser.Parse(string.Empty).Count);
    }
}

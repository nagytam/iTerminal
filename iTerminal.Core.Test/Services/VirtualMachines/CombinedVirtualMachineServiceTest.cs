using iTerminal.Core.Services.VirtualMachines;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.VirtualMachines.Test;

[TestClass]
public class CombinedVirtualMachineServiceTest
{
    private static async Task<InvalidOperationException> ThrownByAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }

        Assert.Fail("The call was expected to throw InvalidOperationException.");
        return null!;
    }

    /// <summary>A backend that lists fixed machines or fails, and records the actions routed to it.</summary>
    private sealed class FakeBackend(string hypervisor, IReadOnlyList<VirtualMachine>? machines, string? error = null) : IVirtualMachineBackend
    {
        public string Hypervisor { get; } = hypervisor;
        public List<(string Name, VirtualMachineAction Action)> Actions { get; } = [];

        public Task<IReadOnlyList<VirtualMachine>> ListAsync(CancellationToken cancellationToken = default) =>
            error is null
                ? Task.FromResult(machines!)
                : Task.FromException<IReadOnlyList<VirtualMachine>>(new InvalidOperationException(error));

        public Task<VirtualMachineActionResult> RunAsync(VirtualMachine machine, VirtualMachineAction action, CancellationToken cancellationToken = default)
        {
            Actions.Add((machine.Name, action));
            return Task.FromResult(new VirtualMachineActionResult(true, $"{machine.Name} in {Hypervisor}"));
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ListsTheMachinesOfEveryBackendThatAnswers()
    {
        var libvirt = new FakeBackend("libvirt", [new VirtualMachine("web", VirtualMachineState.Running, "running", "libvirt")]);
        var boxes = new FakeBackend("GNOME Boxes", [new VirtualMachine("desk", VirtualMachineState.ShutOff, "shut off", "GNOME Boxes")]);
        var service = new CombinedVirtualMachineService([libvirt, boxes]);

        var machines = await service.ListAsync();

        CollectionAssert.AreEqual(
            new[] { ("web", "libvirt"), ("desk", "GNOME Boxes") },
            machines.Select(machine => (machine.Name, machine.Hypervisor)).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ABackendThatFailsIsLeftOutWhileTheOthersStillList()
    {
        var notInstalled = new FakeBackend("VirtualBox", null, "VBoxManage could not be started.");
        var libvirt = new FakeBackend("libvirt", [new VirtualMachine("web", VirtualMachineState.Running, "running", "libvirt")]);
        var service = new CombinedVirtualMachineService([notInstalled, libvirt]);

        var machines = await service.ListAsync();

        Assert.AreEqual("web", Assert.ContainsSingle(machines).Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheListIsAnErrorOnlyWhenNoBackendAnswers()
    {
        var service = new CombinedVirtualMachineService(
        [
            new FakeBackend("libvirt", null, "virsh could not be started."),
            new FakeBackend("VirtualBox", null, "VBoxManage could not be started."),
        ]);

        var exception = await ThrownByAsync(() => service.ListAsync());

        Assert.AreEqual("virsh could not be started.", exception.Message);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptyBackendListSaysNoHypervisorIsAvailable()
    {
        var service = new CombinedVirtualMachineService([]);

        var exception = await ThrownByAsync(() => service.ListAsync());

        StringAssert.Contains(exception.Message, "No virtual machine hypervisor");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnActionGoesBackToTheHypervisorTheMachineWasListedBy()
    {
        var libvirt = new FakeBackend("libvirt", []);
        var virtualBox = new FakeBackend("VirtualBox", []);
        var service = new CombinedVirtualMachineService([libvirt, virtualBox]);

        // Two machines may share a name across hypervisors; the label decides which one is acted on.
        var result = await service.RunAsync(new VirtualMachine("test", VirtualMachineState.ShutOff, "shut off", "VirtualBox"), VirtualMachineAction.Start);

        Assert.AreEqual("test in VirtualBox", result.Message);
        Assert.AreEqual(0, libvirt.Actions.Count);
        CollectionAssert.AreEqual(new[] { ("test", VirtualMachineAction.Start) }, virtualBox.Actions);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnActionForAHypervisorThatIsNotInstalledIsRefused()
    {
        var service = new CombinedVirtualMachineService([new FakeBackend("libvirt", [])]);

        var result = await service.RunAsync(new VirtualMachine("web", VirtualMachineState.Running, "running", "Hyper-V"), VirtualMachineAction.Shutdown);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "no hypervisor named Hyper-V");
    }
}

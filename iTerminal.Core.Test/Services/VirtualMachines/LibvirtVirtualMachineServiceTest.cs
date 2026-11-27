using iTerminal.Core.Services.Processes;
using iTerminal.Core.Services.VirtualMachines;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Services.VirtualMachines.Test;

[TestClass]
public class LibvirtVirtualMachineServiceTest
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

    private static VirtualMachine Vm(string name) => new(name, VirtualMachineState.Running, "running", "test");

    private static readonly string[] Connection = ["-c", "qemu:///system"];

    private static (LibvirtVirtualMachineService Service, Mock<IExternalProcessRunner> Runner, List<(string File, string[] Arguments)> Calls) Create(
        ProcessOutput? output = null,
        Func<string, IReadOnlyList<string>, bool>? launch = null)
    {
        var calls = new List<(string, string[])>();
        var runner = new Mock<IExternalProcessRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, string?, CancellationToken>((file, arguments, _, _) => calls.Add((file, arguments.ToArray())))
            .ReturnsAsync(output ?? new ProcessOutput(0, string.Empty, string.Empty));
        return (new LibvirtVirtualMachineService(runner.Object, launch), runner, calls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ListRunsVirshListAllOnTheSystemConnection()
    {
        var (service, _, calls) = Create(new ProcessOutput(0, " Id   Name   State\n--------------------\n", string.Empty));

        await service.ListAsync();

        var call = Assert.ContainsSingle(calls);
        Assert.AreEqual("virsh", call.File);
        CollectionAssert.AreEqual(new[] { "-c", "qemu:///system", "list", "--all" }, call.Arguments);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(VirtualMachineAction.Start, "start")]
    [DataRow(VirtualMachineAction.Shutdown, "shutdown")]
    [DataRow(VirtualMachineAction.PowerOff, "destroy")]
    [DataRow(VirtualMachineAction.Restart, "reboot")]
    [DataRow(VirtualMachineAction.Pause, "suspend")]
    [DataRow(VirtualMachineAction.Resume, "resume")]
    [DataRow(VirtualMachineAction.SaveState, "managedsave")]
    public async Task EachActionRunsItsVerbOnTheNamedMachine(VirtualMachineAction action, string verb)
    {
        var (service, _, calls) = Create();

        var result = await service.RunAsync(Vm("web"), action);

        Assert.IsTrue(result.Success);
        var call = Assert.ContainsSingle(calls);
        CollectionAssert.AreEqual(new[] { "-c", "qemu:///system", verb, "web" }, call.Arguments);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ANameThatStartsWithADashIsRefusedBeforeAnyCommandRuns()
    {
        var (service, runner, _) = Create();

        var result = await service.RunAsync(Vm("--connect=evil"), VirtualMachineAction.PowerOff);

        Assert.IsFalse(result.Success);
        runner.Verify(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConsoleOpensVirtViewerDetachedOnTheSameConnection()
    {
        string? launched = null;
        IReadOnlyList<string>? launchedWith = null;
        var (service, runner, _) = Create(launch: (file, arguments) =>
        {
            launched = file;
            launchedWith = arguments;
            return true;
        });

        var result = await service.RunAsync(Vm("web"), VirtualMachineAction.Console);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("virt-viewer", launched);
        CollectionAssert.AreEqual(new[] { "-c", "qemu:///system", "web" }, launchedWith!.ToArray());
        runner.Verify(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConsoleSaysWhenVirtViewerIsMissing()
    {
        var (service, _, _) = Create(launch: (_, _) => false);

        var result = await service.RunAsync(Vm("web"), VirtualMachineAction.Console);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "sudo apt install virt-viewer");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PermissionDeniedNamesTheLibvirtGroup()
    {
        var (service, _, _) = Create(new ProcessOutput(1, string.Empty, "error: Failed to connect socket: Permission denied"));

        var result = await service.RunAsync(Vm("web"), VirtualMachineAction.Start);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "libvirt group");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATimeoutSaysTheHypervisorMayStillBeSavingAndToRefresh()
    {
        var (service, _, _) = Create(new ProcessOutput(-1, string.Empty, "virsh did not answer within 30 seconds."));

        var result = await service.RunAsync(Vm("web"), VirtualMachineAction.SaveState);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "press F5");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedListingThrowsASentenceForTheUser()
    {
        var (service, _, _) = Create(new ProcessOutput(1, string.Empty, "error: Permission denied"));

        InvalidOperationException? thrown = null;
        try
        {
            await service.ListAsync();
        }
        catch (InvalidOperationException exception)
        {
            thrown = exception;
        }

        Assert.IsNotNull(thrown, "a failed listing must throw");
        StringAssert.Contains(thrown.Message, "libvirt group");
    }

    private static (LibvirtVirtualMachineService Service, List<(string File, string[] Arguments)> Calls) CreateForBoxes(
        ProcessOutput? output = null,
        Func<string, IReadOnlyList<string>, bool>? launch = null)
    {
        var calls = new List<(string, string[])>();
        var runner = new Mock<IExternalProcessRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, string?, CancellationToken>((file, arguments, _, _) => calls.Add((file, arguments.ToArray())))
            .ReturnsAsync(output ?? new ProcessOutput(0, string.Empty, string.Empty));
        var service = new LibvirtVirtualMachineService(runner.Object, launch,
            LibvirtVirtualMachineService.SessionConnection, LibvirtVirtualMachineService.BoxesLabel);
        return (service, calls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GnomeBoxesUsesTheSessionConnectionAndLabelsItsMachines()
    {
        var (service, calls) = CreateForBoxes(new ProcessOutput(0, " Id   Name   State\n--------------------\n -    desk   shut off\n", string.Empty));

        var machines = await service.ListAsync();

        Assert.AreEqual("GNOME Boxes", service.Hypervisor);
        CollectionAssert.AreEqual(new[] { "-c", "qemu:///session", "list", "--all" }, Assert.ContainsSingle(calls).Arguments);
        var machine = Assert.ContainsSingle(machines);
        Assert.AreEqual(("desk", "GNOME Boxes"), (machine.Name, machine.Hypervisor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GnomeBoxesConsoleOpensVirtViewerOnTheSessionConnection()
    {
        IReadOnlyList<string>? seen = null;
        var (service, _) = CreateForBoxes(launch: (_, arguments) => { seen = arguments; return true; });

        await service.RunAsync(new VirtualMachine("desk", VirtualMachineState.Running, "running", "GNOME Boxes"), VirtualMachineAction.Console);

        CollectionAssert.AreEqual(new[] { "-c", "qemu:///session", "desk" }, seen!.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GnomeBoxesRefusedConnectionDoesNotNameTheLibvirtGroup()
    {
        var (service, _) = CreateForBoxes(new ProcessOutput(1, string.Empty, "error: Failed to connect socket to '/run/user/1000/libvirt/virtqemud-sock'"));

        var exception = await ThrownByAsync(() => service.ListAsync());

        Assert.AreEqual("libvirt refused the connection to qemu:///session.", exception.Message);
    }
}

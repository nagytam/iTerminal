using iTerminal.Core.Services.Processes;
using iTerminal.Core.Services.VirtualMachines;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Services.VirtualMachines.Test;

[TestClass]
public class VirtualBoxVirtualMachineServiceTest
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

    private static VirtualMachine Vm(string name) => new(name, VirtualMachineState.Running, "running");

    private static (VirtualBoxVirtualMachineService Service, List<(string File, string[] Arguments)> Calls) Create(
        ProcessOutput? output = null,
        Func<string, IReadOnlyList<string>, bool>? launch = null)
    {
        var calls = new List<(string, string[])>();
        var runner = new Mock<IExternalProcessRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, string?, CancellationToken>((file, arguments, _, _) => calls.Add((file, arguments.ToArray())))
            .ReturnsAsync(output ?? new ProcessOutput(0, string.Empty, string.Empty));
        return (new VirtualBoxVirtualMachineService(runner.Object, launch), calls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ListRunsVBoxManageListLongVmsAndLabelsEachMachine()
    {
        var (service, calls) = Create(new ProcessOutput(0, "Name:  Ubuntu\nState:  running (since x)\n", string.Empty));

        var machines = await service.ListAsync();

        var call = Assert.ContainsSingle(calls);
        Assert.AreEqual("VBoxManage", call.File);
        CollectionAssert.AreEqual(new[] { "list", "--long", "vms" }, call.Arguments);
        Assert.AreEqual("VirtualBox", Assert.ContainsSingle(machines).Hypervisor);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(VirtualMachineAction.Start, "startvm", "web", "--type", "headless")]
    [DataRow(VirtualMachineAction.Shutdown, "controlvm", "web", "acpipowerbutton")]
    [DataRow(VirtualMachineAction.PowerOff, "controlvm", "web", "poweroff")]
    [DataRow(VirtualMachineAction.Restart, "controlvm", "web", "reset")]
    [DataRow(VirtualMachineAction.Pause, "controlvm", "web", "pause")]
    [DataRow(VirtualMachineAction.Resume, "controlvm", "web", "resume")]
    [DataRow(VirtualMachineAction.SaveState, "controlvm", "web", "savestate")]
    public async Task EachActionRunsItsVBoxManageCommandOnTheNamedMachine(VirtualMachineAction action, params string[] expected)
    {
        var (service, calls) = Create();

        var result = await service.RunAsync(Vm("web"), action);

        Assert.IsTrue(result.Success, result.Message);
        var call = Assert.ContainsSingle(calls);
        Assert.AreEqual("VBoxManage", call.File);
        CollectionAssert.AreEqual(expected, call.Arguments);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ANameThatStartsWithADashIsRefusedBeforeAnyCommandRuns()
    {
        var (service, calls) = Create();

        var result = await service.RunAsync(Vm("--help"), VirtualMachineAction.PowerOff);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, calls.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConsoleAttachesVirtualBoxVmDetachedToTheNamedMachine()
    {
        var launched = new List<(string File, string[] Arguments)>();
        var (service, calls) = Create(launch: (file, arguments) =>
        {
            launched.Add((file, arguments.ToArray()));
            return true;
        });

        var result = await service.RunAsync(Vm("web"), VirtualMachineAction.Console);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, calls.Count, "the console is not a VBoxManage call");
        var launch = Assert.ContainsSingle(launched);
        Assert.AreEqual("VirtualBoxVM", launch.File);
        CollectionAssert.AreEqual(new[] { "--startvm", "web" }, launch.Arguments);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMissingVirtualBoxVmSaysWhereItComesFrom()
    {
        var (service, _) = Create(launch: (_, _) => false);

        var result = await service.RunAsync(Vm("web"), VirtualMachineAction.Console);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "VirtualBoxVM was not found");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMissingVBoxManageSaysToInstallVirtualBox()
    {
        var (service, _) = Create(new ProcessOutput(-1, string.Empty, "No such file or directory"));

        var exception = await ThrownByAsync(() => service.ListAsync());

        StringAssert.Contains(exception.Message, "Install VirtualBox");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATimeoutSaysTheHypervisorMayStillBeWorking()
    {
        var (service, _) = Create(new ProcessOutput(-1, string.Empty, "The process did not answer within 30 seconds."));

        var result = await service.RunAsync(Vm("web"), VirtualMachineAction.SaveState);

        Assert.AreEqual($"web: {LibvirtVirtualMachineService.TimeoutSentence}", result.Message);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedCommandShowsVBoxManagesFirstErrorLine()
    {
        var (service, _) = Create(new ProcessOutput(1, string.Empty, "VBOX_E_INVALID_OBJECT_STATE: The machine is not running\nmore"));

        var result = await service.RunAsync(Vm("web"), VirtualMachineAction.Pause);

        Assert.AreEqual("web: VBOX_E_INVALID_OBJECT_STATE: The machine is not running", result.Message);
    }
}

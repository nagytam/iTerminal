using iTerminal.Core.Services.Processes;
using iTerminal.Core.Services.VirtualMachines;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Services.VirtualMachines.Test;

[TestClass]
public class HyperVVirtualMachineServiceTest
{
    private static (HyperVVirtualMachineService Service, Mock<IExternalProcessRunner> Runner, List<(string File, string[] Arguments)> Calls) Create(
        ProcessOutput? output = null,
        Func<string, IReadOnlyList<string>, bool>? launch = null)
    {
        var calls = new List<(string, string[])>();
        var runner = new Mock<IExternalProcessRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, string?, CancellationToken>((file, arguments, _, _) => calls.Add((file, arguments.ToArray())))
            .ReturnsAsync(output ?? new ProcessOutput(0, string.Empty, string.Empty));
        return (new HyperVVirtualMachineService(runner.Object, launch), runner, calls);
    }

    private static string ScriptOf((string File, string[] Arguments) call) => call.Arguments[^1];

    [Timeout(60_000)]
    [TestMethod]
    public async Task ListRunsGetVmThroughWindowsPowerShellAndParsesTheJson()
    {
        var (service, _, calls) = Create(new ProcessOutput(0, """[{"Name":"web","State":"Running"},{"Name":"db","State":"Off"}]""", string.Empty));

        var machines = await service.ListAsync();

        var call = Assert.ContainsSingle(calls);
        Assert.AreEqual("powershell", call.File);
        CollectionAssert.AreEqual(new[] { "-NoProfile", "-NonInteractive", "-Command" }, call.Arguments[..3]);
        StringAssert.Contains(ScriptOf(call), "Get-VM");
        Assert.HasCount(2, machines);
        Assert.AreEqual(VirtualMachineState.Running, machines[0].State);
        Assert.AreEqual(VirtualMachineState.ShutOff, machines[1].State);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(VirtualMachineAction.Start, "Start-VM")]
    [DataRow(VirtualMachineAction.Shutdown, "Stop-VM -Confirm:$false")]
    [DataRow(VirtualMachineAction.PowerOff, "Stop-VM -TurnOff")]
    [DataRow(VirtualMachineAction.Restart, "Restart-VM -Force")]
    [DataRow(VirtualMachineAction.Pause, "Suspend-VM")]
    [DataRow(VirtualMachineAction.Resume, "Resume-VM")]
    [DataRow(VirtualMachineAction.SaveState, "Save-VM")]
    public async Task EachActionRunsItsCmdletOnTheNamedMachine(VirtualMachineAction action, string cmdlet)
    {
        var (service, _, calls) = Create();

        var result = await service.RunAsync("web", action);

        Assert.IsTrue(result.Success);
        var script = ScriptOf(Assert.ContainsSingle(calls));
        StringAssert.Contains(script, cmdlet);
        StringAssert.Contains(script, "$_.Name -eq 'web'");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheMachineIsFoundByExactNameSoAWildcardInANameMatchesNothingElse()
    {
        var (service, _, calls) = Create();

        await service.RunAsync("a*", VirtualMachineAction.PowerOff);

        var script = ScriptOf(Assert.ContainsSingle(calls));
        Assert.IsFalse(script.Contains("Get-VM -Name", StringComparison.Ordinal), "-Name reads * and ? as wildcards");
        StringAssert.Contains(script, "$vm.Count -ne 1");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASingleQuoteInANameCannotEndTheLiteral()
    {
        var (service, _, calls) = Create();

        await service.RunAsync("it's; Remove-VM x", VirtualMachineAction.Start);

        StringAssert.Contains(ScriptOf(Assert.ContainsSingle(calls)), "$_.Name -eq 'it''s; Remove-VM x'");
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("--evil")]
    [DataRow("-Force")]
    [DataRow("")]
    [DataRow("two\nlines")]
    public async Task ANameThatCouldBeReadAsAnOptionOrHoldsAControlCharacterIsRefusedBeforeAnyCommandRuns(string name)
    {
        var (service, runner, _) = Create();

        var result = await service.RunAsync(name, VirtualMachineAction.PowerOff);

        Assert.IsFalse(result.Success);
        runner.Verify(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConsoleOpensVmConnectOnThisComputerDetached()
    {
        string? launched = null;
        IReadOnlyList<string>? launchedWith = null;
        var (service, runner, _) = Create(launch: (file, arguments) =>
        {
            launched = file;
            launchedWith = arguments;
            return true;
        });

        var result = await service.RunAsync("web", VirtualMachineAction.Console);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("vmconnect", launched);
        CollectionAssert.AreEqual(new[] { "localhost", "web" }, launchedWith!.ToArray());
        runner.Verify(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ConsoleSaysWhenVmConnectIsMissing()
    {
        var (service, _, _) = Create(launch: (_, _) => false);

        var result = await service.RunAsync("web", VirtualMachineAction.Console);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "Hyper-V Management Tools");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMissingHyperVModuleSaysToTurnTheFeatureOn()
    {
        var (service, _, _) = Create(new ProcessOutput(1, string.Empty, HyperVVirtualMachineService.MissingMarker));

        var result = await service.RunAsync("web", VirtualMachineAction.Start);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "Turn on the Hyper-V feature");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARefusalNamesTheAdministratorsGroup()
    {
        var (service, _, _) = Create(new ProcessOutput(1, string.Empty, HyperVVirtualMachineService.DeniedMarker));

        var result = await service.RunAsync("web", VirtualMachineAction.Start);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "Hyper-V Administrators");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMachineThatIsGoneIsReportedAsSuch()
    {
        var (service, _, _) = Create(new ProcessOutput(1, string.Empty, HyperVVirtualMachineService.NoMachineMarker));

        var result = await service.RunAsync("web", VirtualMachineAction.Start);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "no such virtual machine");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATimeoutSaysTheHypervisorMayStillBeWorkingAndToRefresh()
    {
        var (service, _, _) = Create(new ProcessOutput(-1, string.Empty, "powershell did not answer within 30 seconds."));

        var result = await service.RunAsync("web", VirtualMachineAction.SaveState);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "press F5");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnyOtherFailureShowsTheFirstErrorLine()
    {
        var (service, _, _) = Create(new ProcessOutput(1, string.Empty, "The virtual machine is busy.\nsecond line"));

        var result = await service.RunAsync("web", VirtualMachineAction.Start);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "The virtual machine is busy.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedListingThrowsASentenceForTheUser()
    {
        var (service, _, _) = Create(new ProcessOutput(1, string.Empty, HyperVVirtualMachineService.DeniedMarker));

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
        StringAssert.Contains(thrown.Message, "Hyper-V Administrators");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryScriptFirstChecksThatTheHyperVModuleIsThereAndMapsFailuresToMarkers()
    {
        var script = HyperVVirtualMachineService.Wrap("Get-VM");

        StringAssert.Contains(script, "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)");
        StringAssert.Contains(script, "Get-Command Get-VM");
        StringAssert.Contains(script, HyperVVirtualMachineService.MissingMarker);
        StringAssert.Contains(script, "PermissionDenied");
        StringAssert.Contains(script, HyperVVirtualMachineService.DeniedMarker);
        StringAssert.Contains(script, "exit 1");
    }
}

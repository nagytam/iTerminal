using iTerminal.Core.Services.Processes;
using iTerminal.Core.Services.SystemSettings;
using Moq;

namespace iTerminal.Core.Test.Services.SystemSettings;

[TestClass]
public class SystemSettingsServiceTest
{
    private static readonly ProcessOutput Done = new(0, string.Empty, string.Empty);

    private sealed class MemoryStore : IEnvironmentVariableStore
    {
        public Dictionary<string, string> Values { get; } = [];

        public IReadOnlyList<SystemEnvironmentVariable> Read() =>
            [.. Values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new SystemEnvironmentVariable(p.Key, p.Value))];

        public void Set(string name, string value) => Values[name] = value;

        public void Remove(string name) => Values.Remove(name);
    }

    private static (SystemSettingsService Service, Mock<IExternalProcessRunner> Runner, MemoryStore User, MemoryStore Machine) Create(
        bool windows, bool elevated)
    {
        var runner = new Mock<IExternalProcessRunner>();
        runner
            .Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Done);
        var user = new MemoryStore();
        var machine = new MemoryStore();
        return (new SystemSettingsService(runner.Object, () => elevated, windows, linux: !windows, user, machine), runner, user, machine);
    }

    private static void VerifyRan(Mock<IExternalProcessRunner> runner, string file, params string[] arguments) =>
        runner.Verify(r => r.RunAsync(file, It.Is<IReadOnlyList<string>>(a => a.SequenceEqual(arguments)), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

    [TestMethod]
    public async Task LinuxRenamesTheMachineWithHostnamectl()
    {
        var (service, runner, _, _) = Create(windows: false, elevated: true);

        var result = await service.SetComputerNameAsync(" web-01 ");

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsFalse(result.RestartRequired);
        VerifyRan(runner, "hostnamectl", "set-hostname", "web-01");
    }

    [TestMethod]
    public async Task WindowsRenamesWithRenameComputerAndSaysARestartIsNeeded()
    {
        var (service, runner, _, _) = Create(windows: true, elevated: true);

        var result = await service.SetComputerNameAsync("DESK-7");

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(result.RestartRequired);
        runner.Verify(r => r.RunAsync(
            "powershell.exe",
            It.Is<IReadOnlyList<string>>(a => a.Last() == "Rename-Computer -NewName 'DESK-7' -Force -ErrorAction Stop"),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ARenameWithoutAdministratorRightsIsRefusedBeforeAnythingRuns()
    {
        var (service, runner, _, _) = Create(windows: false, elevated: false);

        var result = await service.SetComputerNameAsync("web-01");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "administrator");
        runner.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task AnInvalidNameIsRefusedWithTheReasonAndNothingRuns()
    {
        var (service, runner, _, _) = Create(windows: true, elevated: true);

        var result = await service.SetComputerNameAsync("12345");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "digits");
        runner.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task AFailedToolReportsItsFirstErrorLine()
    {
        var (service, runner, _, _) = Create(windows: false, elevated: true);
        runner
            .Setup(r => r.RunAsync("hostnamectl", It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessOutput(1, string.Empty, "Failed to set hostname: Access denied\nmore"));

        var result = await service.SetComputerNameAsync("web-01");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("Failed to set hostname: Access denied", result.Message);
    }

    [TestMethod]
    public async Task ADescriptionWithAQuoteIsEscapedForPowerShell()
    {
        var (service, runner, _, _) = Create(windows: true, elevated: true);

        var description = await service.SetDescriptionAsync("Tamas' desk");

        Assert.IsTrue(description.Success, description.Message);
        runner.Verify(r => r.RunAsync(
            "powershell.exe",
            It.Is<IReadOnlyList<string>>(a => a.Last().Contains("Description='Tamas'' desk'")),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreEqual("Tamas'' desk", SystemSettingsService.Quote("Tamas' desk"));
    }

    [TestMethod]
    public async Task ADomainMemberCannotBeMovedToAWorkgroupFromHere()
    {
        var (service, runner, _, _) = Create(windows: true, elevated: true);
        runner
            .Setup(r => r.RunAsync("powershell.exe", It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessOutput(0, "DESK\nTrue\ncorp.example\n\n", string.Empty));

        var result = await service.SetMembershipAsync("HOME");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "corp.example");
    }

    [TestMethod]
    public async Task TheWindowsIdentityIsReadFromTheCimClasses()
    {
        var (service, runner, _, _) = Create(windows: true, elevated: false);
        runner
            .Setup(r => r.RunAsync("powershell.exe", It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessOutput(0, "DESK\r\nFalse\r\nHOME\r\nMy desk\r\n", string.Empty));

        var identity = await service.GetComputerAsync();

        Assert.AreEqual(new ComputerIdentity("DESK", "HOME", false, "My desk", true, true), identity);
    }

    [TestMethod]
    public async Task UserVariablesNeedNoAdministratorRightsAndMachineVariablesDo()
    {
        var (service, _, user, machine) = Create(windows: false, elevated: false);

        var own = await service.SetEnvironmentVariableAsync(EnvironmentScope.User, "EDITOR", "vim");
        var shared = await service.SetEnvironmentVariableAsync(EnvironmentScope.Machine, "EDITOR", "vim");

        Assert.IsTrue(own.Success, own.Message);
        Assert.AreEqual("vim", user.Values["EDITOR"]);
        Assert.IsFalse(shared.Success);
        StringAssert.Contains(shared.Message, "administrator");
        Assert.AreEqual(0, machine.Values.Count);
    }

    [TestMethod]
    public async Task AnElevatedProcessChangesAndRemovesMachineVariables()
    {
        var (service, _, _, machine) = Create(windows: true, elevated: true);

        await service.SetEnvironmentVariableAsync(EnvironmentScope.Machine, "JAVA_HOME", "C:\\Java");
        var listed = await service.GetEnvironmentAsync(EnvironmentScope.Machine);
        var removed = await service.RemoveEnvironmentVariableAsync(EnvironmentScope.Machine, "JAVA_HOME");

        CollectionAssert.AreEqual(new[] { "JAVA_HOME" }, listed.Select(v => v.Name).ToArray());
        Assert.IsTrue(removed.Success);
        Assert.AreEqual(0, machine.Values.Count);
    }

    [TestMethod]
    public async Task AnInvalidVariableNameNeverReachesTheStore()
    {
        var (service, _, user, _) = Create(windows: false, elevated: true);

        var result = await service.SetEnvironmentVariableAsync(EnvironmentScope.User, "has space", "x");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, user.Values.Count);
    }

    [TestMethod]
    public async Task AFailingStoreIsAMessageNotACrash()
    {
        var store = new Mock<IEnvironmentVariableStore>();
        store.Setup(s => s.Set(It.IsAny<string>(), It.IsAny<string>())).Throws(new UnauthorizedAccessException("Access to the path is denied."));
        var service = new SystemSettingsService(Mock.Of<IExternalProcessRunner>(), () => true, windows: false, linux: true, store.Object, store.Object);

        var result = await service.SetEnvironmentVariableAsync(EnvironmentScope.User, "A", "1");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("Access to the path is denied.", result.Message);
    }

    [TestMethod]
    public async Task LinuxSetsTheTimeZoneWithTimedatectlAndWindowsWithTzutil()
    {
        var (linux, linuxRunner, _, _) = Create(windows: false, elevated: true);
        var (windows, windowsRunner, _, _) = Create(windows: true, elevated: true);

        Assert.IsTrue((await linux.SetTimeZoneAsync("Europe/Budapest")).Success);
        Assert.IsTrue((await windows.SetTimeZoneAsync("Central Europe Standard Time")).Success);

        VerifyRan(linuxRunner, "timedatectl", "set-timezone", "Europe/Budapest");
        VerifyRan(windowsRunner, "tzutil", "/s", "Central Europe Standard Time");
    }

    [TestMethod]
    public async Task ATimeZoneThatLooksLikeAnOptionIsNotPassedToTheTool()
    {
        var (service, runner, _, _) = Create(windows: false, elevated: true);

        var result = await service.SetTimeZoneAsync("--help");

        Assert.IsFalse(result.Success);
        runner.VerifyNoOtherCalls();
    }

    [TestMethod]
    public void TheSystemTimeZonesAreListedAndIncludeTheCurrentOne()
    {
        var (service, _, _, _) = Create(windows: false, elevated: false);

        var zones = service.GetTimeZones();

        Assert.IsTrue(zones.Count > 0);
        Assert.AreEqual(TimeZoneInfo.Local.Id, service.CurrentTimeZoneId);
    }

    [TestMethod]
    public void EnvironmentSupportFollowsTheStoresThePlatformHas()
    {
        var runner = Mock.Of<IExternalProcessRunner>();

        var none = new SystemSettingsService(runner, () => false, windows: false, linux: false, null, null);

        Assert.IsFalse(none.SupportsEnvironment(EnvironmentScope.User));
        Assert.IsFalse(none.SupportsEnvironment(EnvironmentScope.Machine));
        Assert.IsFalse(none.SupportsTimeZone);
    }
}

using iTerminal.Core.Services;
using iTerminal.Core.Services.Shell;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Services.Shell.Test;

/// <summary>
/// The session service over a real shell: the selection is saved, the session is kept between commands, it follows
/// the panel only when the panel has moved, and a switch waits for a running command. Tests needing a shell are
/// inconclusive when neither sh nor bash is installed.
/// </summary>
[TestClass]
public class LocalShellSessionServiceTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "iterm-shell-svc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "a", "sub"));
        Directory.CreateDirectory(Path.Combine(_root, "b"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static (LocalShellSessionService Service, Mock<ISettingsService> Settings) Create(string? saved = null)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(ShellKinds.SettingKey)).Returns(saved);
        return (new LocalShellSessionService(settings.Object, [new NoSandbox()]), settings);
    }

    private static LocalShellSessionService RequireShell(out Mock<ISettingsService> settings)
    {
        var (service, mock) = Create();
        settings = mock;
        if (!service.IsAvailable)
        {
            Assert.Inconclusive("Neither sh nor bash is installed here.");
        }

        return service;
    }

    [TestMethod]
    public void Switch_SavesTheChoiceAsTheDefault()
    {
        var service = RequireShell(out var settings);
        var others = service.Available().Where(k => k != service.Selected).ToList();
        if (others.Count == 0)
        {
            Assert.Inconclusive("Only one shell is installed, so there is nothing to switch to.");
        }

        var kind = others[0];

        Assert.IsTrue(service.Switch(kind));

        settings.Verify(s => s.Set(ShellKinds.SettingKey, ShellKinds.Name(kind)), Times.Once);
        service.Dispose();
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Switch_IsRefusedWhileACommandRuns()
    {
        var service = RequireShell(out _);
        var current = service.Selected;
        var running = service.RunAsync(_root, "sleep 30");
        await UiTestHelper.WaitUntilAsync(() => service.IsBusy, "the command is running");

        Assert.IsFalse(service.Switch(current), "even the current shell is refused while busy");

        service.Interrupt();
        var result = await running.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.AreEqual(ShellRunState.Interrupted, result.State);
        service.Dispose();
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Variables_PersistAcrossCommandsInTheSameFolder()
    {
        var service = RequireShell(out _);

        await service.RunAsync(Path.Combine(_root, "a"), "SVC_VALUE=7");
        var result = await service.RunAsync(Path.Combine(_root, "a"), "echo $SVC_VALUE");

        Assert.AreEqual("7", result.Output);
        service.Dispose();
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task WhenThePanelHasMoved_TheSessionChangesToIt_AndKeepsItsVariables()
    {
        var service = RequireShell(out _);
        await service.RunAsync(Path.Combine(_root, "a"), "SVC_VALUE=9");

        var result = await service.RunAsync(Path.Combine(_root, "b"), "echo $SVC_VALUE");

        Assert.AreEqual(Path.Combine(_root, "b"), result.Folder);
        Assert.AreEqual("9", result.Output);
        service.Dispose();
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AnUnfollowedPanel_PullsTheSessionBackToIt()
    {
        var service = RequireShell(out _);
        // The command moved the session into a/sub, but the panel was not moved after it (the user had gone elsewhere).
        await service.RunAsync(Path.Combine(_root, "a"), "cd sub");

        var result = await service.RunAsync(Path.Combine(_root, "a"), "true");

        Assert.AreEqual(Path.Combine(_root, "a"), result.Folder);
        service.Dispose();
    }
}

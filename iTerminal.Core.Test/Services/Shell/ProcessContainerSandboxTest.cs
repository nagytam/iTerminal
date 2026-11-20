using iTerminal.Core.Services.Shell;
using Microsoft.Mxc.Sdk.V1;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.Shell.Test;

/// <summary>
/// The process-container sandbox's request and its refusals. Starting a real ProcessContainer needs Windows and the
/// SDK's native library, so the availability and start checks run only where that cannot apply.
/// </summary>
[TestClass]
public class ProcessContainerSandboxTest
{
    private static readonly SessionLaunch Shell = new("C:\\Windows\\System32\\cmd.exe", ["/Q", "/K"]);

    [TestMethod]
    public void Name_IsProcessContainer()
    {
        Assert.AreEqual("process-container", new ProcessContainerSandbox().Name);
    }

    [TestMethod]
    public void Wrap_Refuses_SoTheShellIsNeverRunUnconfined()
    {
        Assert.ThrowsExactly<NotSupportedException>(() => new ProcessContainerSandbox().Wrap(Shell, Path.GetTempPath()));
    }

    [TestMethod]
    public void IsAvailable_IsFalse_OffWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("This check is for machines that are not Windows.");
        }

        Assert.IsFalse(new ProcessContainerSandbox().IsAvailable);
    }

    [TestMethod]
    public void Start_Throws_WhenNotAvailable_AndDoesNotRunTheShell()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Starting a ProcessContainer needs Windows with the SDK's native library.");
        }

        Assert.ThrowsExactly<InvalidOperationException>(() => new ProcessContainerSandbox().Start(Shell, Path.GetTempPath()));
    }

    [TestMethod]
    public void CommandLine_JoinsProgramAndArguments_QuotingOnlyWhereNeeded()
    {
        var line = ProcessContainerSandbox.CommandLine(new SessionLaunch("/bin/sh", ["-c", "echo a b"]));

        Assert.AreEqual("/bin/sh -c \"echo a b\"", line);
    }

    [TestMethod]
    public void CommandLine_EscapesQuotesInsideAnArgument()
    {
        var line = ProcessContainerSandbox.CommandLine(new SessionLaunch("say", ["\"hi\""]));

        Assert.AreEqual("say \"\\\"hi\\\"\"", line);
    }

    [TestMethod]
    public void BuildRequest_ForAnExistingFolder_RunsInAProcessContainer_WithTheFolderWritable()
    {
        var folder = Path.GetTempPath();
        var request = ProcessContainerSandbox.BuildRequest(Shell, folder);

        Assert.IsInstanceOfType<Containment.ProcessContainer>(request.Containment);
        Assert.AreEqual(folder, request.WorkingDirectory);
        Assert.IsNotNull(request.Filesystem);
        CollectionAssert.Contains(request.Filesystem.ReadwritePaths, folder);
        Assert.IsNull(request.TimeoutMs);
    }

    [TestMethod]
    public void BuildRequest_ForAMissingFolder_GivesNoWorkingDirectoryAndNoWritablePath()
    {
        var folder = Path.Combine(Path.GetTempPath(), "iTerminal-missing-" + Guid.NewGuid().ToString("N"));
        var request = ProcessContainerSandbox.BuildRequest(Shell, folder);

        Assert.IsNull(request.WorkingDirectory);
        Assert.IsNull(request.Filesystem);
    }
}

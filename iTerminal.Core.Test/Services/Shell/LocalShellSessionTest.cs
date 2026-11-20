using iTerminal.Core.Services.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.Shell.Test;

/// <summary>
/// The protocol against a real sh and bash, in a temporary folder. A shell that is not installed makes its test
/// inconclusive rather than failing.
/// </summary>
[TestClass]
public class LocalShellSessionTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "iterm-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A shell still holding its folder is ended by its own session's Dispose.
        }
    }

    private LocalShellSession Start(ShellKind kind)
    {
        var program = ShellLaunch.FindOnPath(ShellKinds.Name(kind));
        if (program is null)
        {
            Assert.Inconclusive($"{ShellKinds.Name(kind)} is not installed here.");
        }

        return LocalShellSession.Start(kind, program, _folder, new NoSandbox());
    }

    private static async Task<ShellCommandResult> RunWithin(LocalShellSession session, string command, int seconds = 15) =>
        await session.RunAsync(command).WaitAsync(TimeSpan.FromSeconds(seconds));

    [TestMethod]
    [Timeout(60_000)]
    [DataRow(ShellKind.Sh)]
    [DataRow(ShellKind.Bash)]
    public async Task VariablesPersistAcrossCommands(ShellKind kind)
    {
        using var session = Start(kind);

        await RunWithin(session, "SHELL_TEST_VALUE=42");
        var result = await RunWithin(session, "echo $SHELL_TEST_VALUE");

        Assert.AreEqual(ShellRunState.Completed, result.State);
        Assert.AreEqual("42", result.Output);
    }

    [TestMethod]
    [Timeout(60_000)]
    [DataRow(ShellKind.Sh)]
    [DataRow(ShellKind.Bash)]
    public async Task ChangingDirectory_PersistsAndIsReported(ShellKind kind)
    {
        Directory.CreateDirectory(Path.Combine(_folder, "sub"));
        using var session = Start(kind);

        var result = await RunWithin(session, "cd sub");

        Assert.AreEqual(ShellRunState.Completed, result.State);
        Assert.AreEqual(Path.Combine(_folder, "sub"), result.Folder);
        Assert.AreEqual(Path.Combine(_folder, "sub"), session.Folder);
    }

    [TestMethod]
    [Timeout(60_000)]
    [DataRow(ShellKind.Sh)]
    [DataRow(ShellKind.Bash)]
    public async Task OutputStdErrAndExitCode_AreReportedSeparately(ShellKind kind)
    {
        using var session = Start(kind);

        var result = await RunWithin(session, "echo out; echo err >&2; false");

        Assert.AreEqual("out", result.Output);
        Assert.AreEqual("err", result.Error);
        Assert.AreEqual(1, result.ExitCode);
    }

    [TestMethod]
    [Timeout(60_000)]
    [DataRow(ShellKind.Sh)]
    [DataRow(ShellKind.Bash)]
    public async Task OutputWithoutANewline_IsSplitFromTheMarker(ShellKind kind)
    {
        using var session = Start(kind);

        var result = await RunWithin(session, "printf abc");

        Assert.AreEqual("abc", result.Output);
        Assert.AreEqual(ShellRunState.Completed, result.State);
    }

    [TestMethod]
    [Timeout(60_000)]
    [DataRow(ShellKind.Sh)]
    [DataRow(ShellKind.Bash)]
    public async Task ACompoundCommandThatReadsStdin_DoesNotBlockOnTheProtocol(ShellKind kind)
    {
        using var session = Start(kind);

        // cat would read the protocol pipe if the group did not take its stdin from /dev/null.
        var result = await RunWithin(session, "true && cat");

        Assert.AreEqual(ShellRunState.Completed, result.State);
        Assert.AreEqual(string.Empty, result.Output);
    }

    [TestMethod]
    [Timeout(60_000)]
    [DataRow(ShellKind.Sh)]
    [DataRow(ShellKind.Bash)]
    public async Task Interrupt_EndsTheRun_AndTheSessionIsGone(ShellKind kind)
    {
        using var session = Start(kind);

        // An unterminated quote keeps the marker from being read: the run ends only when the session is ended.
        var pending = session.RunAsync("echo 'open");
        await Task.Delay(300);
        session.Interrupt();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.AreEqual(ShellRunState.Interrupted, result.State);
        await Task.Delay(200);
        Assert.IsTrue(session.HasEnded);
    }

    [TestMethod]
    [Timeout(60_000)]
    [DataRow(ShellKind.Sh)]
    [DataRow(ShellKind.Bash)]
    public async Task Cancellation_EndsALongRun(ShellKind kind)
    {
        using var session = Start(kind);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var result = await session.RunAsync("sleep 30", cancel.Token).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.AreEqual(ShellRunState.Interrupted, result.State);
    }

    [TestMethod]
    [Timeout(60_000)]
    [DataRow(ShellKind.Sh)]
    [DataRow(ShellKind.Bash)]
    public async Task Exit_EndsTheSession_AndReportsItAsEnded(ShellKind kind)
    {
        using var session = Start(kind);

        var result = await RunWithin(session, "exit 3");

        Assert.AreEqual(ShellRunState.Ended, result.State);
        await Task.Delay(200);
        Assert.IsTrue(session.HasEnded);
    }
}

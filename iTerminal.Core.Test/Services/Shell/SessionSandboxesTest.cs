using iTerminal.Core.Services.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.Shell.Test;

/// <summary>
/// What each sandbox does to the shell's launch command, checked on the arguments it builds. The one test that runs
/// a shell inside bubblewrap is inconclusive where bwrap is not installed or cannot make namespaces.
/// </summary>
[TestClass]
public class SessionSandboxesTest
{
    private static readonly SessionLaunch Shell = new("/bin/bash", ["--noprofile", "--norc"]);

    [TestMethod]
    public void Names_AreDistinct_AndNoneIsTheDefault()
    {
        var names = new ISessionSandbox[] { new NoSandbox(), new BubblewrapSandbox(), new BubblewrapOfflineSandbox() }
            .Select(sandbox => sandbox.Name).ToList();

        CollectionAssert.AreEqual(new[] { "none", "bubblewrap", "bubblewrap-offline" }, names);
    }

    [TestMethod]
    public void NoSandbox_LeavesTheShellAsItIs()
    {
        var launch = new NoSandbox().Wrap(Shell, "/work");

        Assert.AreEqual(Shell, launch);
    }

    [TestMethod]
    public void Bubblewrap_RunsTheShellAfterItsOptions_WithTheFolderWritableAndTheRestReadOnly()
    {
        var folder = Path.GetTempPath();
        var launch = new BubblewrapSandbox().Wrap(Shell, folder);
        var arguments = launch.Arguments.ToList();

        Assert.AreEqual("bwrap", launch.Program);
        CollectionAssert.Contains(arguments, "--ro-bind");
        AssertFollows(arguments, "--ro-bind", "/", "/");
        AssertFollows(arguments, "--tmpfs", "/tmp");
        AssertFollows(arguments, "--bind", folder, folder);
        AssertFollows(arguments, "--chdir", folder);
        CollectionAssert.Contains(arguments, "--die-with-parent");
        CollectionAssert.DoesNotContain(arguments, "--unshare-net");

        // The shell and its own arguments come last, after every option.
        CollectionAssert.AreEqual(new[] { "/bin/bash", "--noprofile", "--norc" }, arguments.TakeLast(3).ToArray());
    }

    [TestMethod]
    public void Bubblewrap_BindsTheFolderAfterThePrivateTmp_SoAFolderUnderTmpStaysWritable()
    {
        var arguments = new BubblewrapSandbox().Wrap(Shell, Path.GetTempPath()).Arguments.ToList();

        Assert.IsTrue(arguments.IndexOf("--tmpfs") < arguments.IndexOf("--bind"));
    }

    [TestMethod]
    public void Bubblewrap_WithAMissingFolder_BindsNothingAndStartsTheShellAnyway()
    {
        var missing = Path.Combine(Path.GetTempPath(), "iterm-no-such-" + Guid.NewGuid().ToString("N"));
        var arguments = new BubblewrapSandbox().Wrap(Shell, missing).Arguments;

        CollectionAssert.DoesNotContain(arguments.ToList(), "--bind");
        CollectionAssert.DoesNotContain(arguments.ToList(), "--chdir");
        Assert.AreEqual("/bin/bash", arguments[^3]);
    }

    [TestMethod]
    public void BubblewrapOffline_IsTheSameSandbox_WithTheNetworkUnshared()
    {
        var arguments = new BubblewrapOfflineSandbox().Wrap(Shell, Path.GetTempPath()).Arguments.ToList();

        CollectionAssert.Contains(arguments, "--unshare-net");
        AssertFollows(arguments, "--ro-bind", "/", "/");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task BubblewrapSession_CanWriteItsFolder_ButNotTheRestOfTheSystem()
    {
        var sandbox = new BubblewrapSandbox();
        if (!sandbox.IsAvailable)
        {
            Assert.Inconclusive("bwrap is not installed here.");
        }

        var folder = Path.Combine(Path.GetTempPath(), "iterm-bwrap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var shellProgram = ShellLaunch.FindOnPath("sh") ?? "/bin/sh";
            var session = LocalShellSession.Start(ShellKind.Sh, shellProgram, folder, sandbox);
            try
            {
                // "/" is read-only inside the sandbox, so this probe can never write anywhere, even as root.
                var result = await session.RunAsync(
                    "echo inside > inside.txt && echo wrote-folder; touch /iterm-sandbox-probe 2>/dev/null && echo wrote-root || echo root-read-only")
                    .WaitAsync(TimeSpan.FromSeconds(30));

                if (result.State != ShellRunState.Completed)
                {
                    Assert.Inconclusive("bwrap could not start the shell here (user namespaces may be disabled): " + result.Error);
                }

                StringAssert.Contains(result.Output, "wrote-folder");
                StringAssert.Contains(result.Output, "root-read-only");
                Assert.IsTrue(File.Exists(Path.Combine(folder, "inside.txt")));
            }
            finally
            {
                session.Dispose();
            }
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A file still held open is left for the temporary folder's own cleanup.
            }
        }
    }

    private static void AssertFollows(IReadOnlyList<string> arguments, params string[] sequence)
    {
        for (var index = 0; index + sequence.Length <= arguments.Count; index++)
        {
            if (sequence.Select((word, offset) => arguments[index + offset] == word).All(same => same))
            {
                return;
            }
        }

        Assert.Fail("Expected the arguments to contain: " + string.Join(' ', sequence));
    }
}

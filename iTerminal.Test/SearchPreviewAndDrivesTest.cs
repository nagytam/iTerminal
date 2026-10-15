using iConsole.Data;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Test;

/// <summary>
/// F7 search, Ctrl+F3 quick view, Ctrl+F6 git history and Ctrl+F9 Docker, end to end: the real
/// keys, the real routing, the real panels. The Docker daemon is <see cref="SandboxServices.DockerCli"/>;
/// git is the real git, on a repository the test creates.
/// </summary>
[TestClass]
public class SearchPreviewAndDrivesTest
{
    private TerminalApp _app = null!;

    [TestCleanup]
    public async Task StopAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task F7FindsFilesAndCtrlPListsThemInThePanel()
    {
        _app = await TerminalApp.StartAsync();

        await _app.PressAsync("F7");
        await _app.WaitForTextAsync("Find Files");
        await _app.TypeAsync("inside");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Found 1 in");
        var screen = await _app.WaitForTextAsync(Path.Combine("alpha", "inside_alpha.txt"));
        Assert.IsTrue(screen.Contains("Results (1)"), screen.Dump());

        await _app.PressAsync("Ctrl+P");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForTextAsync("search: inside in");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists(Path.Combine("alpha", "inside_alpha.txt"), s), "the hit to be a panel row");
        await _app.WaitForNoStrayFramesAsync();

        // Enter on the hit goes where it is: its folder, the cursor on it.
        await _app.SelectEntryAsync("inside_alpha.txt");
        await _app.PressAsync("Enter");
        await _app.WaitForFocusedFolderAsync(Path.Combine(_app.LeftRoot, "alpha"));
        await _app.WaitUntilAsync(() => _app.FocusedEntryName == "inside_alpha.txt", "the cursor to be on the file");
    }

    [TestMethod]
    public async Task F7ClosedWithEscLeavesThePanelsAsTheyWere()
    {
        _app = await TerminalApp.StartAsync();

        await _app.PressAsync("F7");
        await _app.WaitForTextAsync("Find Files");
        await _app.PressAsync("Esc");

        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
        Assert.IsTrue(_app.LeftPanelLists("readme.txt"));
    }

    [TestMethod]
    public async Task CtrlF3PreviewsTheFileUnderTheCursorInTheOtherPanel()
    {
        _app = await TerminalApp.StartAsync();
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("Ctrl+F3");

        await _app.WaitForTextAsync("Preview: readme.txt");
        await _app.WaitUntilAsync(s => _app.RightPanel(s).Any(l => l.Contains("Hello from the left panel.")), "the file's text in the right panel");
        Assert.AreEqual("readme.txt", _app.FocusedEntryName, "the keyboard stays in the listing");

        await _app.SelectEntryAsync("notes.md");
        await _app.WaitUntilAsync(s => _app.RightPanel(s).Any(l => l.Contains("# Notes")), "the preview to follow the cursor");

        await _app.PressAsync("Ctrl+F3");
        await _app.WaitUntilAsync(s => _app.RightPanelLists("right_only.txt", s), "the right panel to get its folder back");
        await _app.WaitForNoStrayFramesAsync();
    }

    [TestMethod]
    public async Task CtrlF9BrowsesAContainerAndComesBack()
    {
        _app = await TerminalApp.StartAsync();

        await _app.PressAsync("Ctrl+F9");
        await _app.WaitForTextAsync("docker: containers");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("web", s), "the container to be listed");

        await _app.SelectEntryAsync("web");
        await _app.PressAsync("Enter");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("etc", s), "the container's root");
        await _app.SelectEntryAsync("etc");
        await _app.PressAsync("Enter");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("motd", s), "the file inside the container");

        // A container's file previews like any other: through the docker cp tar stream.
        await _app.SelectEntryAsync("motd");
        await _app.PressAsync("Ctrl+F3");
        await _app.WaitUntilAsync(s => _app.RightPanel(s).Any(l => l.Contains(SandboxServices.DockerCli.Motd)), "the container file's text");
        await _app.PressAsync("Ctrl+F3");

        await _app.PressAsync("Ctrl+F9");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("readme.txt", s), "Ctrl+F9 again to bring the folder back");
    }

    [TestMethod]
    public async Task AContainerIsStoppedStartedAndRestartedFromItsRow()
    {
        _app = await TerminalApp.StartAsync();
        var docker = _app.Driver.GetRequiredService<SandboxServices.DockerCli>();

        await _app.PressAsync("Ctrl+F9");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("web", s), "the container to be listed");
        await _app.SelectEntryAsync("web");
        await _app.WaitForTextAsync("Shift+F9: start/stop");

        await _app.PressAsync("Shift+F9");
        await _app.WaitForTextAsync("web stopped");
        await _app.WaitUntilAsync(s => _app.LeftPanel(s).Any(l => l.Contains("Exited")), "the row to show the stopped container");
        Assert.AreEqual("web", _app.FocusedEntryName, "the cursor stays on the container");

        await _app.PressAsync("Shift+F9");
        await _app.WaitForTextAsync("web started");

        await _app.PressAsync("Ctrl+Shift+F9");
        await _app.WaitForTextAsync("web restarted");
        CollectionAssert.AreEqual(new[] { "stop web", "start web", "restart web" }, docker.Actions.ToArray());

        // A container is not a folder: F8 on its row offers nothing to delete. It used to offer to
        // delete the container's whole file system, "Recursively delete folder" ticked.
        Assert.IsFalse(await _app.PressAsync("F8"), "F8 is not taken on a container row");
        await Task.Delay(300);
        Assert.IsInstanceOfType<Core.Screens.TerminalScreen>(_app.Driver.FocusedModal, "no delete dialog opened");
    }

    [TestMethod]
    public async Task TheQuickViewOfAContainerIsItsLog()
    {
        _app = await TerminalApp.StartAsync();

        await _app.PressAsync("Ctrl+F9");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("web", s), "the container to be listed");
        await _app.SelectEntryAsync("web");

        await _app.PressAsync("Ctrl+F3");
        await _app.WaitUntilAsync(s => _app.RightPanel(s).Any(l => l.Contains(SandboxServices.DockerCli.LastLogLine)), "the container's log");
        await _app.WaitForTextAsync("web — log");
        await _app.PressAsync("Ctrl+F3");
        await _app.WaitUntilAsync(s => _app.RightPanelLists("right_only.txt", s), "the right panel to get its folder back");
    }

    [TestMethod]
    public async Task CtrlOOpensAShellInAContainerAndTheCommandLogRunsInsideItToo()
    {
        _app = await TerminalApp.StartAsync();

        await _app.PressAsync("Ctrl+F9");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("web", s), "the container to be listed");
        await _app.SelectEntryAsync("web");
        await _app.PressAsync("Enter");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("etc", s), "the container's root");
        await _app.SelectEntryAsync("etc");
        await _app.PressAsync("Enter");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("motd", s), "the container's /etc");

        // Ctrl+O: an interactive shell inside the container, in the panel's folder.
        var shell = _app.Driver.GetRequiredService<SandboxServices.Shell>();
        await _app.PressAsync("Ctrl+O");
        await _app.WaitForTextAsync(SandboxServices.Shell.Prompt + "web:/etc");
        Assert.AreEqual("docker", shell.LastLaunch!.FileName);
        CollectionAssert.IsSubsetOf(new[] { "exec", "-it", "-w", "/etc", "web" }, shell.LastLaunch.Arguments.ToArray());
        shell.Exit();
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("motd", s), "the panels to come back after the shell");

        // Ctrl+Shift+O: the command log, whose commands run inside the container one at a time.
        await _app.PressAsync("Ctrl+Shift+O");
        await _app.WaitForTextAsync("web:/etc>");

        await _app.TypeAsync("cat motd");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync(SandboxServices.DockerCli.Motd);

        await _app.PressAsync("Ctrl+Shift+O");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("motd", s), "the panels to come back");
    }

    [TestMethod]
    public async Task CtrlF6BrowsesAnOldRevisionAndF5CopiesItOut()
    {
        if (!GitAvailable())
        {
            Assert.Inconclusive("git not available");
        }

        _app = await TerminalApp.StartAsync(seed: (left, _) =>
        {
            Git(left, "init", "-b", "main");
            Git(left, "config", "user.email", "test@example.com");
            Git(left, "config", "user.name", "Test");
            Git(left, "config", "core.autocrlf", "false");
            File.WriteAllText(Path.Combine(left, "history.txt"), "as committed");
            Git(left, "add", "history.txt");
            Git(left, "commit", "-m", "add history");
            File.WriteAllText(Path.Combine(left, "history.txt"), "changed since");
        });

        await _app.PressAsync("Ctrl+F6");
        await _app.WaitForTextAsync("branches, tags, commits");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("HEAD", s) && _app.LeftPanelLists("main", s), "the revisions");

        await _app.SelectEntryAsync("main");
        await _app.PressAsync("Enter");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("history.txt", s), "the committed tree");
        Assert.IsFalse(_app.LeftPanelLists("readme.txt"), "only what was committed is in the revision");

        await _app.SelectEntryAsync("history.txt");
        await _app.PressAsync("F5");
        await _app.WaitForTextAsync("Copy");
        await _app.PressAsync("Enter");

        var copy = Path.Combine(_app.RightRoot, "history.txt");
        await _app.WaitUntilAsync(() => ReadIfFinished(copy) == "as committed", "the committed version to be copied out");
        Assert.AreEqual("changed since", File.ReadAllText(Path.Combine(_app.LeftRoot, "history.txt")), "the working tree is untouched");
        Assert.IsInstanceOfType<ContentModelGitDrive>(_app.FocusedModel);
    }

    private static string? ReadIfFinished(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool GitAvailable()
    {
        try
        {
            Git(Path.GetTempPath(), "--version");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Git(string folder, params string[] arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var error = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)}: {error.Result}");
        }
    }
}

using iTerminal.Core.Dialogs;
using System.Diagnostics;

namespace iTerminal.Test;

/// <summary>
/// Links in the panels, end to end: a link shows where it leads, Ctrl+L makes one, and F8 on a link
/// to a folder removes the link — not the files of the folder it leads to.
/// </summary>
[TestClass]
public class LinksTest
{
    private TerminalApp _app = null!;

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    /// <summary>A link to a folder any account can make: symbolic where allowed, a junction on Windows otherwise.</summary>
    private static void CreateFolderLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }
        catch (Exception exception) when (OperatingSystem.IsWindows() && exception is IOException or UnauthorizedAccessException)
        {
        }

        var startInfo = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", linkPath, targetPath })
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, "mklink /J failed");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ALinkedFolder_ShowsWhereItLeads_AndF8RemovesOnlyTheLink()
    {
        _app = await TerminalApp.StartAsync(seed: (left, right) =>
        {
            Directory.CreateDirectory(Path.Combine(right, "precious"));
            File.WriteAllText(Path.Combine(right, "precious", "keep.txt"), "do not lose me");
            CreateFolderLink(Path.Combine(left, "shortcut"), Path.Combine(right, "precious"));
        });
        var kept = Path.Combine(_app.RightRoot, "precious", "keep.txt");

        var screen = await _app.WaitForTextAsync("shortcut →");
        Assert.IsTrue(_app.LeftPanelLists("shortcut", screen), screen.Dump());

        // Entered like a folder: the link is a way into the folder it leads to.
        await _app.SelectEntryAsync("shortcut");
        await _app.PressAsync("Enter");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("keep.txt", s), "the linked folder's file to be listed");
        await _app.PressAsync("Home");
        await _app.PressAsync("Enter");
        await _app.WaitForFocusedFolderAsync(_app.LeftRoot);

        // F8 with the dialog as it opens — recursive delete and all.
        await _app.SelectEntryAsync("shortcut");
        await _app.PressAsync("F8");
        await _app.Driver.WaitForModalAsync<DeleteDialog>();
        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(() => !Directory.Exists(Path.Combine(_app.LeftRoot, "shortcut")), "the link to be removed");
        await _app.WaitForNoDialogAsync();
        Assert.AreEqual("do not lose me", File.ReadAllText(kept), "the folder the link led to is untouched");
        await _app.WaitUntilAsync(s => !_app.LeftPanelLists("shortcut", s), "the link to leave the listing");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task DeletingAFolderThatHoldsALink_LeavesTheLinkedFoldersFilesAlone()
    {
        _app = await TerminalApp.StartAsync(seed: (left, right) =>
        {
            Directory.CreateDirectory(Path.Combine(right, "precious"));
            File.WriteAllText(Path.Combine(right, "precious", "keep.txt"), "do not lose me");
            CreateFolderLink(Path.Combine(left, "alpha", "shortcut"), Path.Combine(right, "precious"));
        });
        var kept = Path.Combine(_app.RightRoot, "precious", "keep.txt");

        await _app.SelectEntryAsync("alpha");
        await _app.PressAsync("F8");
        var dialog = await _app.Driver.WaitForModalAsync<DeleteDialog>();
        Assert.IsTrue(dialog.Recursive, "the box that makes this dangerous is ticked as the dialog opens");
        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(() => !Directory.Exists(Path.Combine(_app.LeftRoot, "alpha")), "the folder to be deleted");
        Assert.AreEqual("do not lose me", File.ReadAllText(kept));
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlL_MakesAHardLinkInTheOtherPanel()
    {
        _app = await TerminalApp.StartAsync();
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("Ctrl+L");
        var dialog = await _app.Driver.WaitForModalAsync<CreateLinkDialog>();
        await _app.WaitForTextAsync("Create link");
        Assert.AreEqual(Path.Combine(_app.RightRoot, "readme.txt"), dialog.LinkPath, "offered in the other panel, as F5 offers its copy");
        Assert.AreEqual(Path.Combine(_app.LeftRoot, "readme.txt"), dialog.TargetPath);

        // Tab to the hard-link box and tick it.
        await _app.PressAsync("Tab");
        await _app.PressAsync("Space");
        Assert.IsTrue(dialog.IsHardLink);
        await _app.PressAsync("Enter");

        await _app.WaitForNoDialogAsync();
        var link = Path.Combine(_app.RightRoot, "readme.txt");
        Assert.IsTrue(File.Exists(link), _app.Screen.Dump());
        File.AppendAllText(link, " And more.");
        StringAssert.EndsWith(File.ReadAllText(Path.Combine(_app.LeftRoot, "readme.txt")), " And more.", "two names, one file");
        await _app.WaitUntilAsync(s => _app.RightPanelLists("readme.txt", s), "the link to be listed in the other panel");
        await _app.WaitForNoStrayFramesAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlL_MakesASymbolicLink_OrSaysWhyItMayNot()
    {
        _app = await TerminalApp.StartAsync();
        await _app.SelectEntryAsync("alpha");

        await _app.PressAsync("Ctrl+L");
        var dialog = await _app.Driver.WaitForModalAsync<CreateLinkDialog>();
        Assert.IsTrue(dialog.TargetIsFolder);
        await _app.PressAsync("Enter");
        await dialog.CreateTask.ContinueWith(_ => { });

        var link = Path.Combine(_app.RightRoot, "alpha");
        if (Directory.Exists(link))
        {
            await _app.WaitForNoDialogAsync();
            Assert.IsTrue(File.Exists(Path.Combine(link, "inside_alpha.txt")), "the link leads into the folder");
            await _app.WaitForTextAsync("alpha →");
        }
        else
        {
            // Windows without Developer Mode: the dialog stays, and says what to do.
            await _app.WaitForTextAsync("Developer Mode");
            Assert.IsInstanceOfType<CreateLinkDialog>(_app.Driver.FocusedModal);
        }
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ALinkIsNotMadeOverAnExistingFile()
    {
        _app = await TerminalApp.StartAsync(seed: (left, right) => File.WriteAllText(Path.Combine(right, "readme.txt"), "already here"));
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("Ctrl+L");
        await _app.Driver.WaitForModalAsync<CreateLinkDialog>();
        await _app.PressAsync("Enter");

        await _app.WaitForTextAsync("already exists");
        Assert.IsInstanceOfType<CreateLinkDialog>(_app.Driver.FocusedModal);
        Assert.AreEqual("already here", File.ReadAllText(Path.Combine(_app.RightRoot, "readme.txt")));
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlL_IsNotOfferedOnTheParentRow()
    {
        _app = await TerminalApp.StartAsync();
        await _app.PressAsync("Home");

        Assert.IsFalse(await _app.PressAsync("Ctrl+L"));
        Assert.IsInstanceOfType<Core.Screens.TerminalScreen>(_app.Driver.FocusedModal);
    }
}

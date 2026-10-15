using iTerminal.Core.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace iTerminal.Test;

/// <summary>
/// Ctrl+C/X/V on files through the (in-process, in the sandbox) file clipboard, and the disk
/// manager's drive actions against a recording fake — neither may touch the machine running them.
/// </summary>
[TestClass]
public class ClipboardAndDriveActionsTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    private Core.Services.Clipboard.IFileClipboardService Clipboard =>
        _app.Driver.GetRequiredService<Core.Services.Clipboard.IFileClipboardService>();

    private SandboxServices.DriveActions Drives => _app.Driver.GetRequiredService<SandboxServices.DriveActions>();

    // ---- clipboard -----------------------------------------------------------------------------

    [TestMethod]
    public async Task CtrlCThenCtrlVCopiesAFileToTheOtherPanel()
    {
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("Ctrl+C");

        await _app.WaitUntilAsync(() => Clipboard.GetFilesAsync().Result is not null, "the clipboard to hold the file");
        var content = (await Clipboard.GetFilesAsync())!;
        Assert.IsFalse(content.IsCut);
        CollectionAssert.AreEqual(new[] { Path.Combine(_app.LeftRoot, "readme.txt") }, content.Paths.ToArray());
        Assert.IsFalse(_app.Driver.HasExited, "Ctrl+C on a file copies; it does not quit");

        await _app.PressAsync("Tab");
        await _app.PressAsync("Ctrl+V");

        await _app.WaitUntilAsync(() => File.Exists(Path.Combine(_app.RightRoot, "readme.txt")), "the pasted copy");
        Assert.IsTrue(File.Exists(Path.Combine(_app.LeftRoot, "readme.txt")), "a copy leaves the original");
        await _app.WaitUntilAsync(s => _app.RightPanelLists("readme.txt", s), "the target panel to list the paste");
    }

    [TestMethod]
    public async Task CtrlXThenCtrlVMovesAFile()
    {
        await _app.SelectEntryAsync("notes.md");

        await _app.PressAsync("Ctrl+X");

        await _app.WaitUntilAsync(() => Clipboard.GetFilesAsync().Result is { IsCut: true }, "the clipboard to hold a cut");
        Assert.IsTrue(File.Exists(Path.Combine(_app.LeftRoot, "notes.md")), "a cut touches nothing until the paste");

        await _app.PressAsync("Tab");
        await _app.PressAsync("Ctrl+V");

        await _app.WaitUntilAsync(() => File.Exists(Path.Combine(_app.RightRoot, "notes.md")), "the moved file");
        await _app.WaitUntilAsync(() => !File.Exists(Path.Combine(_app.LeftRoot, "notes.md")), "the original to be gone");
    }

    [TestMethod]
    public async Task PastingIntoTheFolderACopyCameFromNamesItLikeExplorer()
    {
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Ctrl+C");
        await _app.WaitUntilAsync(() => Clipboard.GetFilesAsync().Result is not null, "the clipboard to hold the file");

        await _app.PressAsync("Ctrl+V");

        await _app.WaitUntilAsync(() => File.Exists(Path.Combine(_app.LeftRoot, "readme - Copy.txt")), "a 'readme - Copy.txt'");
    }

    [TestMethod]
    public async Task TheCuaKeysWorkToo()
    {
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("Ctrl+Insert");
        await _app.WaitUntilAsync(() => Clipboard.GetFilesAsync().Result is { IsCut: false }, "Ctrl+Insert to copy");

        await _app.PressAsync("Tab");
        await _app.PressAsync("Shift+Insert");

        await _app.WaitUntilAsync(() => File.Exists(Path.Combine(_app.RightRoot, "readme.txt")), "Shift+Insert to paste");
    }

    [TestMethod]
    public async Task CtrlCOnTheParentRowStillQuits()
    {
        await _app.PressAsync("Home");

        await _app.PressAsync("Ctrl+C");

        await _app.Driver.WaitForExitAsync();
    }

    [TestMethod]
    public async Task CtrlVWithNothingOnTheClipboardSaysSo()
    {
        await _app.PressAsync("Ctrl+V");

        await _app.WaitForTextAsync("holds no files");
    }

    // ---- drive actions -------------------------------------------------------------------------

    private async Task<DiskManagerDialog> OpenDiskManagerAsync()
    {
        await _app.PressAsync("Alt+F9");
        await _app.WaitForTextAsync("Disk manager");
        var dialog = (DiskManagerDialog)_app.Driver.FocusedModal!;
        await _app.WaitUntilAsync(() => dialog.SelectedDrive is not null, "a drive under the cursor");

        // The memory drive is listed first and has no volume to act on: walk down to a real one.
        for (var step = 0; step < 10 && dialog.SelectedDrive?.DriveInfo is null; step++)
        {
            await _app.PressAsync("Down");
        }

        Assert.IsNotNull(dialog.SelectedDrive?.DriveInfo, "the machine has no drive to act on");
        return dialog;
    }

    [TestMethod]
    public async Task TheDiskManagerShowsTheDriveUnderTheCursorAndItsBitLockerState()
    {
        var dialog = await OpenDiskManagerAsync();

        await _app.WaitUntilAsync(() => dialog.DetailsText.Contains("Off (sandbox)"), "BitLocker status in the details");
        StringAssert.Contains(dialog.DetailsText, "BitLocker");
        await _app.WaitForTextAsync("E:eject");
    }

    [TestMethod]
    public async Task EEjectsTheDriveUnderTheCursor()
    {
        var dialog = await OpenDiskManagerAsync();
        var root = dialog.SelectedDrive!.DriveInfo!.Name;

        await _app.PressAsync("e");

        await _app.WaitUntilAsync(() => Drives.Calls.Contains($"eject {root}"), $"an eject of {root}");
        await _app.WaitUntilAsync(() => dialog.Status.Contains("removed safely"), "the status to say so");
    }

    [TestMethod]
    public async Task FOpensAFormatDialogThatAsksForTheLetterBack()
    {
        var dialog = await OpenDiskManagerAsync();
        var root = dialog.SelectedDrive!.DriveInfo!.Name;

        await _app.PressAsync("f");
        await _app.WaitForTextAsync("Format drive");
        var format = (FormatDriveDialog)_app.Driver.FocusedModal!;

        await format.FormatAsync();
        StringAssert.Contains(format.Error, "to confirm");
        Assert.AreEqual(0, Drives.Calls.Count(c => c.StartsWith("format")), "nothing is formatted without the confirmation");

        format.Confirmation = root[..1];
        Assert.IsTrue(await format.FormatAsync());

        Assert.IsTrue(Drives.Calls.Any(c => c.StartsWith($"format {root}")), string.Join("; ", Drives.Calls));
    }

    [TestMethod]
    public async Task AFormatTheServiceRefusesIsSaidInTheDiskManager()
    {
        Drives.FormatRefusal = "That is the system drive.";
        var dialog = await OpenDiskManagerAsync();

        await _app.PressAsync("f");

        await _app.WaitUntilAsync(() => dialog.Status.Contains("system drive"), "the refusal in the status");
        Assert.IsNotInstanceOfType<FormatDriveDialog>(_app.Driver.FocusedModal);
    }

    [TestMethod]
    public async Task MMountsAnImageByPath()
    {
        await OpenDiskManagerAsync();
        var image = Path.Combine(_app.LeftRoot, "disc.iso");
        File.WriteAllText(image, "not really an iso");

        await _app.PressAsync("m");
        await _app.WaitForTextAsync("Disk image");
        var dialog = (DiskImageDialog)_app.Driver.FocusedModal!;
        dialog.ImagePath = image;

        Assert.IsTrue(await dialog.RunAsync(mount: true));

        CollectionAssert.Contains(Drives.Calls.ToList(), $"mount {image}");
    }

    [TestMethod]
    public async Task ACursorOnAnImageOffersToMountIt()
    {
        var image = Path.Combine(_app.LeftRoot, "disc.iso");
        File.WriteAllText(image, "not really an iso");
        await _app.PressAsync("Ctrl+R");
        await _app.SelectEntryAsync("disc.iso");

        await _app.PressAsync("Ctrl+Shift+F11");
        await _app.WaitForTextAsync("Disk image");
        var dialog = (DiskImageDialog)_app.Driver.FocusedModal!;

        Assert.AreEqual(image, dialog.ImagePath);
    }
}

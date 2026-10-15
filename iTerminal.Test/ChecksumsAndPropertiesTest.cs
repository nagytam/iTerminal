using iTerminal.Core.Dialogs;
using System.Security.Cryptography;

namespace iTerminal.Test;

/// <summary>Shift+F12 checksums and Ctrl+F12 properties, end to end, on the sandbox's own files.</summary>
[TestClass]
public class ChecksumsAndPropertiesTest
{
    private TerminalApp _app = null!;

    [TestCleanup]
    public async Task StopAsync()
    {
        // A test that skips itself before starting an app (Assert.Inconclusive below) has none to stop.
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private string ReadmePath => Path.Combine(_app.LeftRoot, "readme.txt");

    private string ReadmeSha256 => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(ReadmePath)));

    private async Task<ChecksumDialog> OpenChecksumsAsync(string entry)
    {
        await _app.SelectEntryAsync(entry);
        await _app.PressAsync("Shift+F12");
        var dialog = await _app.Driver.WaitForModalAsync<ChecksumDialog>();
        await dialog.ComputeTask;
        return dialog;
    }

    [TestMethod]
    public async Task ShiftF12_ShowsTheFilesChecksums()
    {
        _app = await TerminalApp.StartAsync();

        var dialog = await OpenChecksumsAsync("readme.txt");

        var screen = await _app.WaitForTextAsync(ReadmeSha256);
        Assert.IsTrue(screen.Contains("MD5"), screen.Dump());
        Assert.IsTrue(screen.Contains("SHA-512"), screen.Dump());
        Assert.IsTrue(screen.Contains(Convert.ToHexStringLower(MD5.HashData(File.ReadAllBytes(ReadmePath)))), screen.Dump());
        Assert.IsNull(dialog.Written, "nothing is written down beside this file");

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }

    [TestMethod]
    public async Task ATypedChecksum_IsComparedAsItIsTyped()
    {
        _app = await TerminalApp.StartAsync();
        await OpenChecksumsAsync("readme.txt");
        await _app.WaitForTextAsync(ReadmeSha256);

        await _app.TypeAsync(ReadmeSha256[..20]);
        await _app.WaitForTextAsync("Not a checksum yet: 20 characters");

        await _app.TypeAsync(ReadmeSha256[20..]);
        await _app.WaitForTextAsync("✓ Matches the SHA-256.");

        // One character off is a different file.
        await _app.PressAsync("Backspace");
        await _app.TypeAsync(ReadmeSha256[^1] == '0' ? "1" : "0");
        await _app.WaitForTextAsync("✗ Does NOT match the SHA-256.");
    }

    [TestMethod]
    public async Task F2_WritesAChecksumFile_ThatIsCheckedTheNextTime()
    {
        _app = await TerminalApp.StartAsync();
        await OpenChecksumsAsync("readme.txt");
        await _app.WaitForTextAsync(ReadmeSha256);

        await _app.PressAsync("F2");

        await _app.WaitForTextAsync("Saved readme.txt.sha256.");
        Assert.AreEqual($"{ReadmeSha256} *readme.txt\n", File.ReadAllText(ReadmePath + ".sha256"));
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();

        // Opened again, the file written beside it is found and checked without being asked.
        var dialog = await OpenChecksumsAsync("readme.txt");
        await _app.WaitForTextAsync("✓ matches readme.txt.sha256");
        Assert.AreEqual("readme.txt.sha256", dialog.Written?.Source);
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();

        // The file changes; the checksum written down no longer fits, and F2 will not paper over it.
        File.AppendAllText(ReadmePath, "tampered");
        await OpenChecksumsAsync("readme.txt");
        await _app.WaitForTextAsync("✗ does NOT match readme.txt.sha256");
        await _app.PressAsync("F2");
        await _app.WaitForTextAsync("says something else");
        Assert.AreNotEqual(ReadmeSha256, File.ReadAllText(ReadmePath + ".sha256")[..64], "the old checksum is still the one on disk");
    }

    [TestMethod]
    public async Task ChecksumsAreNotOfferedOnAFolder()
    {
        _app = await TerminalApp.StartAsync();
        await _app.SelectEntryAsync("alpha");

        Assert.IsFalse(await _app.PressAsync("Shift+F12"));
        Assert.IsInstanceOfType<Core.Screens.TerminalScreen>(_app.Driver.FocusedModal);
    }

    [TestMethod]
    public async Task CtrlF12_ShowsAFilesProperties()
    {
        _app = await TerminalApp.StartAsync();
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("Ctrl+F12");
        var dialog = await _app.Driver.WaitForModalAsync<PropertiesDialog>();

        var screen = await _app.WaitForTextAsync("Properties of readme.txt");
        var length = new FileInfo(ReadmePath).Length;
        Assert.IsTrue(screen.Contains($"({length} bytes)"), screen.Dump());
        Assert.IsTrue(screen.Contains("Modified"), screen.Dump());
        Assert.IsTrue(screen.Contains(_app.LeftRoot[^20..]), "its folder, shortened from the middle if need be" + Environment.NewLine + screen.Dump());
        Assert.AreEqual(length, dialog.Properties?.Size);

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }

    [TestMethod]
    public async Task CtrlF12_OnAFolder_AddsUpWhatIsInIt()
    {
        _app = await TerminalApp.StartAsync(seed: (left, _) =>
        {
            Directory.CreateDirectory(Path.Combine(left, "alpha", "deep"));
            File.WriteAllText(Path.Combine(left, "alpha", "deep", "more.txt"), new string('x', 1000));
        });
        await _app.SelectEntryAsync("alpha");

        await _app.PressAsync("Ctrl+F12");
        var dialog = await _app.Driver.WaitForModalAsync<PropertiesDialog>();
        await dialog.MeasureTask;

        // inside_alpha.txt ("inside alpha", 12 bytes) and deep/more.txt.
        await _app.WaitForTextAsync("2 files, 1 folder");
        var screen = await _app.WaitForTextAsync("(1,012 bytes)");
        Assert.IsFalse(screen.Contains("so far"), screen.Dump());
        Assert.IsTrue(screen.Contains("Folder"), screen.Dump());
    }

    [TestMethod]
    public async Task AnAttributeTickedInTheDialog_IsWrittenOnEnter()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The attribute boxes are the Windows form of the dialog.");
        }

        _app = await TerminalApp.StartAsync();
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Ctrl+F12");
        var dialog = await _app.Driver.WaitForModalAsync<PropertiesDialog>();
        await _app.WaitForTextAsync("Read-only");
        Assert.IsFalse(dialog.IsReadOnlyChecked);

        try
        {
            // The cursor opens on the first box: Read-only.
            await _app.PressAsync("Space");
            Assert.IsTrue(dialog.IsReadOnlyChecked);
            await _app.PressAsync("Enter");

            await _app.WaitForNoDialogAsync();
            Assert.IsTrue(File.GetAttributes(ReadmePath).HasFlag(FileAttributes.ReadOnly));

            // And read back the next time the dialog opens.
            await _app.SelectEntryAsync("readme.txt");
            await _app.PressAsync("Ctrl+F12");
            dialog = await _app.Driver.WaitForModalAsync<PropertiesDialog>();
            Assert.IsTrue(dialog.IsReadOnlyChecked);
        }
        finally
        {
            File.SetAttributes(ReadmePath, FileAttributes.Normal);
        }
    }

    [TestMethod]
    public async Task PropertiesAreNotOfferedOnTheParentRow()
    {
        _app = await TerminalApp.StartAsync();
        await _app.PressAsync("Home");

        Assert.IsFalse(await _app.PressAsync("Ctrl+F12"));
        Assert.IsInstanceOfType<Core.Screens.TerminalScreen>(_app.Driver.FocusedModal);
    }
}

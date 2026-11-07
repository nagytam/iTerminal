using iTerminal.Core.Dialogs;

namespace iTerminal.Test;

/// <summary>Ctrl+Shift+E file encryption, end to end, on the sandbox's own files.</summary>
[TestClass]
public class FileEncryptionTest
{
    private const string Passphrase = "e2e passphrase";

    private TerminalApp _app = null!;

    [TestCleanup]
    public async Task StopAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private string ReadmePath => Path.Combine(_app.LeftRoot, "readme.txt");

    private async Task<FileEncryptionDialog> OpenAsync(string entry)
    {
        await _app.SelectEntryAsync(entry);
        await _app.PressAsync("Ctrl+Shift+E");
        var dialog = await _app.Driver.WaitForModalAsync<FileEncryptionDialog>();
        await _app.WaitForTextAsync("Passphrase");
        await _app.WaitUntilAsync(() => dialog.IsPassphraseFocused, "the passphrase box takes the focus");
        return dialog;
    }

    /// <summary>Types the passphrase (the box has focus on open), then the confirmation when there is one.</summary>
    private async Task TypePassphraseAsync(string passphrase, string? confirmation)
    {
        await _app.TypeAsync(passphrase);
        if (confirmation is not null)
        {
            await _app.PressAsync("Tab");
            await _app.TypeAsync(confirmation);
        }
    }

    [TestMethod]
    [Timeout(90_000)] // A hang guard: each test boots the whole app, and the key derivation runs twice.
    public async Task CtrlShiftE_EncryptsAFile_KeepsTheOriginalOnN_ThenDecryptsItBack()
    {
        _app = await TerminalApp.StartAsync();
        var original = await File.ReadAllBytesAsync(ReadmePath);

        var dialog = await OpenAsync("readme.txt");
        await TypePassphraseAsync(Passphrase, Passphrase);
        var typed = await _app.WaitForTextAsync("●");
        Assert.IsFalse(string.Join(" ", typed.Lines).Contains(Passphrase), "the passphrase is never drawn");
        await _app.PressAsync("Enter");
        await dialog.RunTask;

        await _app.WaitForTextAsync("y/n");
        Assert.IsTrue(File.Exists(ReadmePath + ".enc"));
        Assert.IsFalse(File.ReadAllText(ReadmePath + ".enc").Contains("Hello from the left panel"), "the file on disk is ciphertext");
        Assert.IsTrue(File.Exists(ReadmePath), "the original stays until asked about");

        await _app.PressAsync("n");
        await _app.WaitForTextAsync("kept");
        Assert.IsTrue(File.Exists(ReadmePath));
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForTextAsync("File encryption finished");

        // Take the original away and bring it back from the .enc: the dialog opens on Decrypt for a .enc file.
        File.Delete(ReadmePath);
        await _app.WaitForTextAsync("readme.txt.enc");
        dialog = await OpenAsync("readme.txt.enc");
        Assert.AreEqual(Core.Services.Encryption.EncryptionMode.Decrypt, dialog.Mode);
        await TypePassphraseAsync(Passphrase, null);
        await _app.PressAsync("Enter");
        await dialog.RunTask;

        await _app.WaitForTextAsync("y/n");
        await _app.PressAsync("n");
        await _app.WaitForTextAsync("Done: 1 file(s), 0 failed");
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(ReadmePath));
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task CtrlShiftE_DifferentConfirmation_IsRefused_AndNothingIsWritten()
    {
        _app = await TerminalApp.StartAsync();
        await OpenAsync("readme.txt");
        await TypePassphraseAsync("one", "two");

        await _app.PressAsync("Enter");

        await _app.WaitForTextAsync("do not match");
        Assert.IsFalse(File.Exists(ReadmePath + ".enc"));
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
    }

    [TestMethod]
    [Timeout(90_000)]
    public async Task CtrlShiftE_WrongPassphrase_SaysSo_AndLeavesNoPlaintext()
    {
        _app = await TerminalApp.StartAsync();
        var dialog = await OpenAsync("readme.txt");
        await TypePassphraseAsync(Passphrase, Passphrase);
        await _app.PressAsync("Enter");
        await dialog.RunTask;
        await _app.WaitForTextAsync("y/n");
        await _app.PressAsync("n");
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        File.Delete(ReadmePath);
        await _app.WaitForTextAsync("readme.txt.enc");

        dialog = await OpenAsync("readme.txt.enc");
        await TypePassphraseAsync("not the passphrase", null);
        await _app.PressAsync("Enter");
        await dialog.RunTask;

        await _app.WaitForTextAsync("Wrong passphrase");
        Assert.IsFalse(File.Exists(ReadmePath));
        Assert.IsTrue(File.Exists(ReadmePath + ".enc"));
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
    }
}

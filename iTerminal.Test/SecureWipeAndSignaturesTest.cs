using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Signing;

namespace iTerminal.Test;

/// <summary>Ctrl+Shift+F8 secure wipe and Ctrl+Shift+F7 signatures, end to end, on the sandbox's own files.</summary>
[TestClass]
public class SecureWipeAndSignaturesTest
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

    private string ReadmePath => Path.Combine(_app.LeftRoot, "readme.txt");

    [TestMethod]
    [Timeout(45_000)] // A hang guard: each test boots the whole app.
    public async Task CtrlShiftF8_AsksTwice_ThenWipesTheFileAndSaysSo()
    {
        _app = await TerminalApp.StartAsync();
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("Ctrl+Shift+F8");
        var dialog = await _app.Driver.WaitForModalAsync<SecureWipeDialog>();
        await _app.WaitForTextAsync("cannot be undone");

        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Press Enter again");
        Assert.IsTrue(File.Exists(ReadmePath), "one Enter must not destroy anything");

        await _app.PressAsync("Enter");
        await dialog.WipeTask;

        await _app.WaitForTextAsync("Done: 1 file(s)");
        Assert.IsFalse(File.Exists(ReadmePath));
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForTextAsync("Secure wipe finished");
        await _app.WaitForNoStrayFramesAsync();
    }

    [TestMethod]
    [Timeout(45_000)] // A hang guard: each test boots the whole app.
    public async Task CtrlShiftF8_AnyOtherKeyAfterTheFirstEnter_WithdrawsIt()
    {
        _app = await TerminalApp.StartAsync();
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Ctrl+Shift+F8");
        await _app.Driver.WaitForModalAsync<SecureWipeDialog>();
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Press Enter again");

        await _app.PressAsync("Space");

        await _app.WaitForTextAsync("Enter: continue");
        Assert.IsTrue(File.Exists(ReadmePath));
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        Assert.IsTrue(File.Exists(ReadmePath));
    }

    [TestMethod]
    [Timeout(45_000)] // A hang guard: each test boots the whole app.
    public async Task CtrlShiftF7_ReportsAFileWithNoSignature()
    {
        _app = await TerminalApp.StartAsync();
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("Ctrl+Shift+F7");
        var dialog = await _app.Driver.WaitForModalAsync<SignatureDialog>();
        await dialog.WorkTask;

        await _app.WaitForTextAsync("No signature found");
        await _app.WaitForTextAsync("readme.txt");
        Assert.AreEqual(SignatureStatus.NotSigned, dialog.Verdicts.First().Status);
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }

    [TestMethod]
    [Timeout(45_000)] // A hang guard: each test boots the whole app.
    public async Task CtrlShiftF7_ASignatureFileMadeWithAKey_IsFoundAndChecked()
    {
        _app = await TerminalApp.StartAsync();
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var privateKey = Path.Combine(_app.LeftRoot, "me.pem");
        var publicKey = Path.Combine(_app.LeftRoot, "me.pub.pem");
        File.WriteAllText(privateKey, rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(publicKey, rsa.ExportSubjectPublicKeyInfoPem());
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Ctrl+Shift+F7");
        var dialog = await _app.Driver.WaitForModalAsync<SignatureDialog>();
        await dialog.WorkTask;

        // The box takes the private key file; F4 signs with it.
        await _app.TypeAsync(privateKey);
        await _app.PressAsync("F4");
        await dialog.WorkTask;
        await _app.WaitForTextAsync("Wrote readme.txt.sig.");
        Assert.IsTrue(File.Exists(ReadmePath + ".sig"));

        // Then the public key into the box and F5 checks it.
        dialog.Key = publicKey;
        await _app.PressAsync("F5");
        await dialog.WorkTask;
        await _app.WaitForTextAsync("Good RSA signature.");
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }
}

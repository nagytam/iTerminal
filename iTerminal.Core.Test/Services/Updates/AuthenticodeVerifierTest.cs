using iTerminal.Core.Services.Updates;

namespace iTerminal.Core.Test.Services.Updates;

[TestClass]
public class AuthenticodeVerifierTest
{
    [TestMethod]
    public void AnUnsignedFileIsReportedAsNotSigned()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("WinVerifyTrust is Windows only.");
        }

        string path = Path.Combine(Path.GetTempPath(), $"iTerminal.unsigned.{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "not signed");
        try
        {
            AuthenticodeResult result = new WinVerifyTrustAuthenticodeVerifier().Verify(path);

            Assert.AreEqual(AuthenticodeStatus.NotSigned, result.Status);
            Assert.AreEqual(string.Empty, result.Signer);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ASignedSystemFileIsValid()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("WinVerifyTrust is Windows only.");
        }

        // kernel32.dll carries an embedded signature on every Windows install (unlike, say, notepad.exe,
        // which is signed through a catalog and therefore answers NotSigned).
        string path = Path.Combine(Environment.SystemDirectory, "kernel32.dll");
        if (!File.Exists(path))
        {
            Assert.Inconclusive("kernel32.dll was not found.");
        }

        AuthenticodeResult result = new WinVerifyTrustAuthenticodeVerifier().Verify(path);

        Assert.AreEqual(AuthenticodeStatus.Valid, result.Status, result.Message);
    }

    [TestMethod]
    public void AMissingFileIsUnavailable()
    {
        AuthenticodeResult result = new WinVerifyTrustAuthenticodeVerifier()
            .Verify(Path.Combine(Path.GetTempPath(), $"iTerminal.missing.{Guid.NewGuid():N}.msi"));

        Assert.AreEqual(AuthenticodeStatus.Unavailable, result.Status);
    }

    [TestMethod]
    public void OffWindowsTheCheckIsUnavailable()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("This asserts the other platform's answer.");
        }

        AuthenticodeResult result = new WinVerifyTrustAuthenticodeVerifier()
            .Verify(Path.Combine(Path.GetTempPath(), $"iTerminal.{Guid.NewGuid():N}.msi"));

        Assert.AreEqual(AuthenticodeStatus.Unavailable, result.Status);
    }

    [TestMethod]
    public void TheWinVerifyTrustAnswersAreMappedToVerdicts()
    {
        // No signature, or a file type that cannot carry one.
        Assert.AreEqual(AuthenticodeStatus.NotSigned, WinVerifyTrustAuthenticodeVerifier.Interpret(0x800B0100).Status);
        Assert.AreEqual(AuthenticodeStatus.NotSigned, WinVerifyTrustAuthenticodeVerifier.Interpret(0x800B0003).Status);

        // The file was changed after signing, or the signer is not trusted.
        Assert.AreEqual(AuthenticodeStatus.Invalid, WinVerifyTrustAuthenticodeVerifier.Interpret(0x80096010).Status);
        Assert.AreEqual(AuthenticodeStatus.Invalid, WinVerifyTrustAuthenticodeVerifier.Interpret(0x800B0004).Status);
        Assert.AreEqual(AuthenticodeStatus.Invalid, WinVerifyTrustAuthenticodeVerifier.Interpret(0x800B0111).Status);
        Assert.AreEqual(AuthenticodeStatus.Invalid, WinVerifyTrustAuthenticodeVerifier.Interpret(0x800B0109).Status);

        // Anything else is a check that could not be made, never "unsigned".
        Assert.AreEqual(AuthenticodeStatus.Unavailable, WinVerifyTrustAuthenticodeVerifier.Interpret(0x80000000).Status);

        // Success.
        Assert.AreEqual(AuthenticodeStatus.Valid, WinVerifyTrustAuthenticodeVerifier.Interpret(0).Status);
    }
}

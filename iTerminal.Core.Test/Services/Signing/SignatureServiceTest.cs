using iTerminal.Core.Services.Signing;
using iTerminal.Core.Test.Helper;
using System.Security.Cryptography;

namespace iTerminal.Core.Test.Services.Signing;

[TestClass]
public class SignatureServiceTest
{
    private string _root = null!;

    [TestInitialize]
    public void CreateSandbox() => _root = Directory.CreateTempSubdirectory("iTerminal.Signing.").FullName;

    [TestCleanup]
    public void DeleteSandbox()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A runner that says every file is unsigned to Authenticode, so only the detached signatures speak.</summary>
    private static FakeProcessRunner Runner() =>
        new FakeProcessRunner().Answer("pwsh", "NotSigned\n\nThe file is not signed.").Answer("powershell", "NotSigned\n\nThe file is not signed.");

    #region gpg status
    [TestMethod]
    [Timeout(15_000)]
    public void ParseGpgStatus_AGoodSignature_NamesTheSigner()
    {
        var verdict = SignatureService.ParseGpgStatus("[GNUPG:] NEWSIG\n[GNUPG:] GOODSIG 1A2B3C4D5E6F7788 Ada Lovelace <ada@example.org>\n[GNUPG:] VALIDSIG ...", 0, "");

        Assert.AreEqual(SignatureStatus.Valid, verdict.Status);
        Assert.AreEqual("Ada Lovelace <ada@example.org> (1A2B3C4D5E6F7788)", verdict.Signer);
    }

    [TestMethod]
    [Timeout(15_000)]
    public void ParseGpgStatus_ABadSignature_IsInvalid()
    {
        var verdict = SignatureService.ParseGpgStatus("[GNUPG:] BADSIG 1A2B3C4D5E6F7788 Ada", 1, "");

        Assert.AreEqual(SignatureStatus.Invalid, verdict.Status);
    }

    [TestMethod]
    [Timeout(15_000)]
    public void ParseGpgStatus_ARevokedKey_IsNotTrusted()
    {
        Assert.AreEqual(SignatureStatus.Invalid, SignatureService.ParseGpgStatus("[GNUPG:] REVKEYSIG AAAA Ada", 1, "").Status);
    }

    [TestMethod]
    [Timeout(15_000)]
    public void ParseGpgStatus_AnExpiredKey_StillMatchesButSaysSo()
    {
        var verdict = SignatureService.ParseGpgStatus("[GNUPG:] EXPKEYSIG AAAA Ada", 0, "");

        Assert.AreEqual(SignatureStatus.Valid, verdict.Status);
        StringAssert.Contains(verdict.Message, "expired");
    }

    [TestMethod]
    [Timeout(15_000)]
    public void ParseGpgStatus_AKeyNotInTheKeyring_IsAnUnknownSignerWithTheIdToFetch()
    {
        var verdict = SignatureService.ParseGpgStatus("[GNUPG:] ERRSIG AAAA 1 8 00 1 9 01 AAAA\n[GNUPG:] NO_PUBKEY AAAA1111BBBB2222", 2, "");

        Assert.AreEqual(SignatureStatus.UnknownSigner, verdict.Status);
        StringAssert.Contains(verdict.Message, "AAAA1111BBBB2222");
    }

    [TestMethod]
    [Timeout(15_000)]
    public void ParseGpgStatus_NotASignatureAtAll_IsNotSigned()
    {
        Assert.AreEqual(SignatureStatus.NotSigned, SignatureService.ParseGpgStatus("[GNUPG:] NODATA 3", 2, "").Status);
    }

    [TestMethod]
    [Timeout(15_000)]
    public void ParseGpgStatus_NoVerdict_FallsBackToTheTool()
    {
        var verdict = SignatureService.ParseGpgStatus(string.Empty, 2, "gpg: something broke");

        Assert.AreEqual(SignatureStatus.Unavailable, verdict.Status);
        Assert.AreEqual("gpg: something broke", verdict.Message);
    }
    #endregion

    #region Verify
    [TestMethod]
    [Timeout(15_000)]
    public async Task VerifyAsync_ASignatureBesideTheFile_IsCheckedWithGpgAndNothingIsSplicedIntoTheCommand()
    {
        var file = Write("release.zip", "x");
        Write("release.zip.asc", "-----BEGIN PGP SIGNATURE-----\n");
        var runner = Runner().Answer("gpg", "[GNUPG:] GOODSIG AAAA Ada");

        var verdicts = await new SignatureService(runner).VerifyAsync(file);

        var gpg = verdicts.Single(v => v.Method == SignatureMethod.Gpg);
        Assert.AreEqual(SignatureStatus.Valid, gpg.Status);
        var call = runner.Calls.Single(c => c.FileName == "gpg");
        CollectionAssert.AreEqual(new[] { "--status-fd", "1", "--batch", "--no-tty", "--verify", file + ".asc", file }, call.Arguments.ToArray());
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task VerifyAsync_NoSignatureAnywhere_SaysSoOnce()
    {
        var file = Write("plain.txt", "x");

        var verdicts = await new SignatureService(Runner()).VerifyAsync(file);

        Assert.AreEqual(1, verdicts.Count);
        Assert.AreEqual(SignatureStatus.NotSigned, verdicts[0].Status);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task VerifyAsync_GpgNotInstalled_SaysThatInsteadOfBadSignature()
    {
        var file = Write("a.bin", "x");
        Write("a.bin.asc", "-----BEGIN PGP SIGNATURE-----\n");
        var runner = Runner().Answer("gpg", string.Empty, exitCode: -1, stderr: "not found");

        var verdicts = await new SignatureService(runner).VerifyAsync(file);

        var gpg = verdicts.Single(v => v.Method == SignatureMethod.Gpg);
        Assert.AreEqual(SignatureStatus.Unavailable, gpg.Status);
        StringAssert.Contains(gpg.Message, "not installed");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task VerifyAsync_AMissingFile_IsUnavailable()
    {
        var verdicts = await new SignatureService(Runner()).VerifyAsync(Path.Combine(_root, "nope"));

        Assert.AreEqual(SignatureStatus.Unavailable, verdicts.Single().Status);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task VerifyAsync_AnExplicitSignatureFile_IsUsedInsteadOfTheOnesBeside()
    {
        var file = Write("a.bin", "x");
        var elsewhere = Write("detached-somewhere", "-----BEGIN PGP SIGNATURE-----\n");
        var runner = Runner().Answer("gpg", "[GNUPG:] BADSIG AAAA Ada", exitCode: 1);

        var verdicts = await new SignatureService(runner).VerifyAsync(file, elsewhere);

        Assert.AreEqual(SignatureStatus.Invalid, verdicts.Single().Status);
        Assert.AreEqual(elsewhere, runner.Calls.Single(c => c.FileName == "gpg").Arguments[^2]);
    }
    #endregion

    #region gpg signing
    [TestMethod]
    [Timeout(15_000)]
    public async Task SignAsync_Gpg_WritesADetachedArmouredSignatureWithTheChosenKey()
    {
        var file = Write("a.bin", "x");
        var runner = new FakeProcessRunner().AnswerWith("gpg", () =>
        {
            File.WriteAllText(file + ".asc", "sig");
            return string.Empty;
        });

        var result = await new SignatureService(runner).SignAsync(file, SignatureMethod.Gpg, "ada@example.org");

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(file + ".asc", result.SignaturePath);
        CollectionAssert.AreEqual(
            new[] { "--batch", "--yes", "--armor", "--detach-sign", "--local-user", "ada@example.org", "--output", file + ".asc", file },
            runner.Calls.Single().Arguments.ToArray());
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task SignAsync_Gpg_NeverOverwritesASignatureFile()
    {
        var file = Write("a.bin", "x");
        Write("a.bin.asc", "the publisher's");
        var runner = new FakeProcessRunner();

        var result = await new SignatureService(runner).SignAsync(file, SignatureMethod.Gpg);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, runner.Calls.Count);
        Assert.AreEqual("the publisher's", File.ReadAllText(file + ".asc"));
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task SignAsync_Gpg_AKeyIdThatLooksLikeAnOption_IsRefused()
    {
        var file = Write("a.bin", "x");
        var runner = new FakeProcessRunner();

        var result = await new SignatureService(runner).SignAsync(file, SignatureMethod.Gpg, "--homedir /tmp/evil");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task SignAsync_Gpg_AFailure_ReportsGpgsFirstLine()
    {
        var file = Write("a.bin", "x");
        var runner = new FakeProcessRunner().Answer("gpg", string.Empty, exitCode: 2, stderr: "gpg: no default secret key");

        var result = await new SignatureService(runner).SignAsync(file, SignatureMethod.Gpg);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("gpg: no default secret key", result.Message);
    }
    #endregion

    #region Authenticode
    [TestMethod]
    [Timeout(15_000)]
    public void ParseAuthenticode_MapsPowerShellsStatuses()
    {
        Assert.AreEqual(SignatureStatus.Valid, SignatureService.ParseAuthenticode("Valid\nCN=Acme\nSignature verified.").Status);
        Assert.AreEqual(SignatureStatus.NotSigned, SignatureService.ParseAuthenticode("NotSigned\n\nnot signed").Status);
        Assert.AreEqual(SignatureStatus.Invalid, SignatureService.ParseAuthenticode("HashMismatch\nCN=Acme\nchanged").Status);
        Assert.AreEqual(SignatureStatus.UnknownSigner, SignatureService.ParseAuthenticode("NotTrusted\nCN=Acme\nroot not trusted").Status);
        Assert.AreEqual(SignatureStatus.Unavailable, SignatureService.ParseAuthenticode("Surprise\n\n").Status);
        // A file type that cannot carry a signature at all (a .txt) comes back as an error with no signer.
        Assert.AreEqual(SignatureStatus.NotSigned, SignatureService.ParseAuthenticode("UnknownError\n\nThe form specified for the subject is not one supported.").Status);
        Assert.AreEqual("CN=Acme", SignatureService.ParseAuthenticode("Valid\nCN=Acme\nok").Signer);
    }

    [TestMethod]
    [Timeout(15_000)]
    public void ParseAuthenticode_AFailedScript_IsUnavailable()
    {
        Assert.AreEqual(SignatureStatus.Unavailable, SignatureService.ParseAuthenticode(string.Empty, "boom").Status);
    }

    [TestMethod]
    [Timeout(15_000)]
    public void Literal_DoublesTheQuoteSoNothingInAFileNameCanEndIt()
    {
        Assert.AreEqual("'it''s''; Remove-Item *'", SignatureService.Literal("it's'; Remove-Item *"));
        Assert.AreEqual("'plain'", SignatureService.Literal("plain"));
    }

    [TestMethod]
    [Timeout(15_000)]
    public void ParseCertificates_ReadsThumbprintExpiryAndSubject_AndSkipsNoise()
    {
        var text = "0123456789ABCDEF0123456789ABCDEF01234567|2030-01-02T03:04:05.0000000Z|CN=Acme, O=Acme\nWARNING: something\nnot-a-thumbprint|2030-01-02T03:04:05Z|CN=X";

        var certificates = SignatureService.ParseCertificates(text);

        Assert.AreEqual(1, certificates.Count);
        Assert.AreEqual("0123456789ABCDEF0123456789ABCDEF01234567", certificates[0].Thumbprint);
        Assert.AreEqual("CN=Acme, O=Acme", certificates[0].Subject);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task SignAsync_Authenticode_RefusesAThumbprintThatIsNotHex()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Authenticode signing is Windows-only");
        }

        var file = Write("a.exe", "x");
        var runner = new FakeProcessRunner();

        var result = await new SignatureService(runner).SignAsync(file, SignatureMethod.Authenticode, "'; calc; '");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, runner.Calls.Count, "nothing typed reaches PowerShell unless it is a thumbprint");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task SignAsync_Authenticode_PutsTheThumbprintAndThePathInAsLiterals()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Authenticode signing is Windows-only");
        }

        var file = Write("it's.exe", "x");
        var runner = new FakeProcessRunner().Answer("pwsh", "Signature verified.");

        var result = await new SignatureService(runner).SignAsync(file, SignatureMethod.Authenticode, "0123456789abcdef0123456789abcdef01234567");

        Assert.IsTrue(result.Success, result.Message);
        var script = runner.Calls.Single().Arguments[^1];
        StringAssert.Contains(script, "'0123456789ABCDEF0123456789ABCDEF01234567'");
        StringAssert.Contains(script, "'" + file.Replace("'", "''") + "'");
    }
    #endregion

    #region Key file signatures
    private static (string Private, string Public) NewRsa()
    {
        using var rsa = RSA.Create(2048);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }

    private static (string Private, string Public) NewEcdsa()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportPkcs8PrivateKeyPem(), ecdsa.ExportSubjectPublicKeyInfoPem());
    }

    [TestMethod]
    [Timeout(15_000)]
    [DataRow("rsa")]
    [DataRow("ecdsa")]
    public async Task SignAsync_Pem_ThenVerifyAsync_RoundTrips(string algorithm)
    {
        var (privateKey, publicKey) = algorithm == "rsa" ? NewRsa() : NewEcdsa();
        var file = Write("doc.txt", "hello");
        var privatePath = Write("private.pem", privateKey);
        var publicPath = Write("public.pem", publicKey);
        var service = new SignatureService(Runner());

        var signed = await service.SignAsync(file, SignatureMethod.Pem, keyPath: privatePath);
        var verdicts = await service.VerifyAsync(file, publicKeyPath: publicPath);

        Assert.IsTrue(signed.Success, signed.Message);
        Assert.AreEqual(file + ".sig", signed.SignaturePath);
        var verdict = verdicts.Single(v => v.Method == SignatureMethod.Pem);
        Assert.AreEqual(SignatureStatus.Valid, verdict.Status, verdict.Message);
        StringAssert.StartsWith(verdict.Signer, "SHA256:");
        StringAssert.Contains(verdict.Message, algorithm.ToUpperInvariant());
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task VerifyAsync_Pem_AChangedFile_IsInvalid()
    {
        var (privateKey, publicKey) = NewEcdsa();
        var file = Write("doc.txt", "hello");
        var service = new SignatureService(Runner());
        await service.SignAsync(file, SignatureMethod.Pem, keyPath: Write("private.pem", privateKey));
        File.WriteAllText(file, "hello, tampered");

        var verdicts = await service.VerifyAsync(file, publicKeyPath: Write("public.pem", publicKey));

        Assert.AreEqual(SignatureStatus.Invalid, verdicts.Single(v => v.Method == SignatureMethod.Pem).Status);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task VerifyAsync_Pem_AnotherKey_IsInvalid()
    {
        var (privateKey, _) = NewRsa();
        var (_, strangerPublic) = NewRsa();
        var file = Write("doc.txt", "hello");
        var service = new SignatureService(Runner());
        await service.SignAsync(file, SignatureMethod.Pem, keyPath: Write("private.pem", privateKey));

        var verdicts = await service.VerifyAsync(file, publicKeyPath: Write("stranger.pem", strangerPublic));

        Assert.AreEqual(SignatureStatus.Invalid, verdicts.Single(v => v.Method == SignatureMethod.Pem).Status);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task VerifyAsync_Pem_WithoutAPublicKey_AsksForOne()
    {
        var (privateKey, _) = NewRsa();
        var file = Write("doc.txt", "hello");
        var service = new SignatureService(Runner());
        await service.SignAsync(file, SignatureMethod.Pem, keyPath: Write("private.pem", privateKey));

        var verdicts = await service.VerifyAsync(file);

        var verdict = verdicts.Single(v => v.Method == SignatureMethod.Pem);
        Assert.AreEqual(SignatureStatus.UnknownSigner, verdict.Status);
        StringAssert.Contains(verdict.Message, "public key");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task SignAsync_Pem_NeverOverwritesASignature()
    {
        var (privateKey, _) = NewRsa();
        var file = Write("doc.txt", "hello");
        Write("doc.txt.sig", "keep me");

        var result = await new SignatureService(Runner()).SignAsync(file, SignatureMethod.Pem, keyPath: Write("private.pem", privateKey));

        Assert.IsFalse(result.Success);
        Assert.AreEqual("keep me", File.ReadAllText(file + ".sig"));
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task SignAsync_Pem_AnEncryptedKey_IsPointedAtGpg()
    {
        using var rsa = RSA.Create(2048);
        var encrypted = rsa.ExportEncryptedPkcs8PrivateKeyPem("pw", new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000));
        var file = Write("doc.txt", "hello");

        var result = await new SignatureService(Runner()).SignAsync(file, SignatureMethod.Pem, keyPath: Write("enc.pem", encrypted));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "gpg");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task SignAsync_Pem_NotAKey_FailsInWords()
    {
        var file = Write("doc.txt", "hello");

        var result = await new SignatureService(Runner()).SignAsync(file, SignatureMethod.Pem, keyPath: Write("junk.pem", "not a key"));

        Assert.IsFalse(result.Success);
        Assert.IsFalse(File.Exists(file + ".sig"));
    }
    #endregion
}

using iTerminal.Core.Services.Checksums;
using iTerminal.Core.Services.Processes;
using iTerminal.Core.Test.Helper;
using Moq;
using System.Security.Cryptography;

namespace iTerminal.Core.Test.Services.Checksums;

/// <summary>Publisher signatures beside a file: which tool reads which file, what it said, and the guards around the call.</summary>
[TestClass]
public class PublisherSignatureServiceTest
{
    private const string Pgp = "-----BEGIN PGP SIGNATURE-----\n\nabc\n-----END PGP SIGNATURE-----\n";
    private string _folder = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _folder = Path.Combine(Path.GetTempPath(), "publisher-sig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_folder, true);

    private string Write(string name, string text = "x")
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static string Sha256Of(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string Good(string key = "ABCD1234", string user = "Ann <ann@example.org>") => $"[GNUPG:] GOODSIG {key} {user}\n[GNUPG:] VALIDSIG {key}\n";

    [Timeout(30_000)]
    [TestMethod]
    public async Task AGoodGpgSignatureOnTheList_IsGood_AndCarriesTheListHash()
    {
        string file = Write("tool.zip");
        string list = Write("SHA256SUMS", "list");
        Write("SHA256SUMS.asc", Pgp);
        FakeProcessRunner runner = new FakeProcessRunner().Answer("gpg --status-fd 1", Good());

        IReadOnlyList<PublisherVerification> found = await new PublisherSignatureService(runner).VerifyAsync(file, "SHA256SUMS");

        PublisherVerification verification = found.Single();
        Assert.AreEqual(PublisherSignatureStatus.Good, verification.Status);
        Assert.AreEqual("SHA256SUMS", verification.Subject);
        Assert.AreEqual(PublisherTool.Gpg, verification.Tool);
        StringAssert.Contains(verification.Signer, "Ann");
        Assert.AreEqual(Sha256Of(list), verification.SubjectSha256);
        FakeProcessRunner.Call call = runner.Calls.Single();
        Assert.AreEqual("--verify -- " + Path.Combine(_folder, "SHA256SUMS.asc") + " " + list, string.Join(' ', call.Arguments.Skip(4)), "paths are single arguments after --");
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task ASignatureOnTheFileItself_IsFound_WithoutAList()
    {
        string file = Write("tool.zip");
        Write("tool.zip.asc", Pgp);
        FakeProcessRunner runner = new FakeProcessRunner().Answer("gpg", Good());

        PublisherVerification verification = (await new PublisherSignatureService(runner).VerifyAsync(file, null)).Single();

        Assert.AreEqual("tool.zip", verification.Subject);
        Assert.IsNull(verification.SubjectSha256, "a file is not hashed again: it may be an ISO");
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task TheListsSignatureIsLookedForBeforeTheFilesOwn()
    {
        string file = Write("tool.zip");
        Write("SHA256SUMS");
        Write("SHA256SUMS.sig", Pgp);
        Write("tool.zip.asc", Pgp);
        FakeProcessRunner runner = new FakeProcessRunner().Answer("gpg", Good());

        IReadOnlyList<PublisherVerification> found = await new PublisherSignatureService(runner).VerifyAsync(file, "SHA256SUMS");

        CollectionAssert.AreEqual(new[] { "SHA256SUMS", "tool.zip" }, found.Select(v => v.Subject).ToArray());
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task NoSignatureBesideTheFile_GivesNothing_AndRunsNothing()
    {
        string file = Write("tool.zip");
        FakeProcessRunner runner = new();

        IReadOnlyList<PublisherVerification> found = await new PublisherSignatureService(runner).VerifyAsync(file, null);

        Assert.AreEqual(0, found.Count);
        Assert.AreEqual(0, runner.Calls.Count);
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task ABadGpgSignature_IsBad()
    {
        string file = Write("tool.zip");
        Write("tool.zip.asc", Pgp);
        FakeProcessRunner runner = new FakeProcessRunner().Answer("gpg", "[GNUPG:] BADSIG ABCD1234 Ann\n", exitCode: 1);

        Assert.AreEqual(PublisherSignatureStatus.Bad, (await new PublisherSignatureService(runner).VerifyAsync(file, null)).Single().Status);
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task AKeyNotInTheKeyring_IsUnknownKey_WithItsId()
    {
        string file = Write("tool.zip");
        Write("tool.zip.asc", Pgp);
        FakeProcessRunner runner = new FakeProcessRunner().Answer("gpg", "[GNUPG:] ERRSIG ABCD1234 1 8 00 1 9 01 ABCD1234\n[GNUPG:] NO_PUBKEY ABCD1234\n", exitCode: 2);

        PublisherVerification verification = (await new PublisherSignatureService(runner).VerifyAsync(file, null)).Single();

        Assert.AreEqual(PublisherSignatureStatus.UnknownKey, verification.Status);
        Assert.AreEqual("ABCD1234", verification.Signer);
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task AnExpiredKey_IsItsOwnStatus_NotGood()
    {
        string file = Write("tool.zip");
        Write("tool.zip.asc", Pgp);
        FakeProcessRunner runner = new FakeProcessRunner().Answer("gpg", "[GNUPG:] EXPKEYSIG ABCD1234 Ann\n");

        Assert.AreEqual(PublisherSignatureStatus.ExpiredKey, (await new PublisherSignatureService(runner).VerifyAsync(file, null)).Single().Status);
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task ARevokedKey_IsBad()
    {
        string file = Write("tool.zip");
        Write("tool.zip.asc", Pgp);
        FakeProcessRunner runner = new FakeProcessRunner().Answer("gpg", "[GNUPG:] REVKEYSIG ABCD1234 Ann\n");

        Assert.AreEqual(PublisherSignatureStatus.Bad, (await new PublisherSignatureService(runner).VerifyAsync(file, null)).Single().Status);
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task GpgWithNoVerdict_IsAnError()
    {
        string file = Write("tool.zip");
        Write("tool.zip.asc", Pgp);
        FakeProcessRunner runner = new FakeProcessRunner().Answer("gpg", string.Empty, exitCode: 2, stderr: "gpg: something odd");

        PublisherVerification verification = (await new PublisherSignatureService(runner).VerifyAsync(file, null)).Single();

        Assert.AreEqual(PublisherSignatureStatus.Error, verification.Status);
        StringAssert.Contains(verification.Detail, "something odd");
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task AToolThatIsNotInstalled_IsToolMissing()
    {
        string file = Write("tool.zip");
        Write("tool.zip.asc", Pgp);
        Write("tool.zip.minisig", "untrusted comment: x\n");
        FakeProcessRunner runner = new FakeProcessRunner().Answer("gpg", string.Empty, exitCode: -1, stderr: "not found").Answer("minisign", string.Empty, exitCode: -1, stderr: "not found");

        IReadOnlyList<PublisherVerification> found = await new PublisherSignatureService(runner).VerifyAsync(file, null);

        Assert.IsTrue(found.All(v => v.Status == PublisherSignatureStatus.ToolMissing));
        CollectionAssert.AreEquivalent(new[] { "gpg", "minisign" }, found.Select(v => v.Detail).ToArray());
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task AGoodMinisignSignature_IsGood_UsingTheUsersOwnKey()
    {
        string file = Write("tool.zip");
        Write("tool.zip.minisig", "untrusted comment: x\n");
        // A key lying beside the file must never be used: the call names none.
        Write("minisign.pub", "untrusted comment: planted\n");
        FakeProcessRunner runner = new FakeProcessRunner().Answer("minisign -V", "Signature and comment signature verified\nTrusted comment: release 1.2\n");

        PublisherVerification verification = (await new PublisherSignatureService(runner).VerifyAsync(file, null)).Single();

        Assert.AreEqual(PublisherSignatureStatus.Good, verification.Status);
        Assert.AreEqual("release 1.2", verification.Signer);
        Assert.IsFalse(runner.Calls.Single().Arguments.Contains("-p"));
        Assert.IsFalse(runner.Calls.Single().CommandLine.Contains("minisign.pub"));
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task AFailedMinisignCheck_IsBad()
    {
        string file = Write("tool.zip");
        Write("tool.zip.minisig", "untrusted comment: x\n");
        FakeProcessRunner runner = new FakeProcessRunner().Answer("minisign", string.Empty, exitCode: 1, stderr: "Signature verification failed");

        Assert.AreEqual(PublisherSignatureStatus.Bad, (await new PublisherSignatureService(runner).VerifyAsync(file, null)).Single().Status);
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task MinisignWithNoKeyConfigured_IsUnknownKey()
    {
        string file = Write("tool.zip");
        Write("tool.zip.minisig", "untrusted comment: x\n");
        FakeProcessRunner runner = new FakeProcessRunner().Answer("minisign", string.Empty, exitCode: 1, stderr: "Error while loading the public key file");

        PublisherVerification verification = (await new PublisherSignatureService(runner).VerifyAsync(file, null)).Single();

        Assert.AreEqual(PublisherSignatureStatus.UnknownKey, verification.Status);
        Assert.AreEqual(string.Empty, verification.Signer);
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task ASigstoreBundle_IsFoundButNeverRunBlindly()
    {
        string file = Write("tool.zip");
        Write("tool.zip.bundle", "{\"mediaType\":\"x\"}");
        FakeProcessRunner runner = new();

        PublisherVerification verification = (await new PublisherSignatureService(runner).VerifyAsync(file, null)).Single();

        Assert.AreEqual(PublisherSignatureStatus.Unchecked, verification.Status);
        Assert.AreEqual(PublisherTool.Sigstore, verification.Tool);
        Assert.AreEqual(0, runner.Calls.Count);
    }

    [Timeout(30_000)]
    [TestMethod]
    public void ASigFile_IsToldApartByItsContents()
    {
        string pem = Write("a.sig", "iTerminal-signature-v1 rsa\nAAAA");
        string armor = Write("b.sig", Pgp);
        string binary = Write("c.sig", "x");
        File.WriteAllBytes(binary, [0x89, 0x01, 0x33, 0x04]);
        string cosign = Write("d.sig", "MEUCIQDabcdefghijklmnopqrstuvwxyz0123456789==\n");
        string mini = Write("e.sig", "untrusted comment: signature from minisign secret key\nRW");

        Assert.IsNull(PublisherSignatureService.PickTool(pem, ".sig"), "this program's own signature belongs to the signatures dialog");
        Assert.AreEqual(PublisherTool.Gpg, PublisherSignatureService.PickTool(armor, ".sig"));
        Assert.AreEqual(PublisherTool.Gpg, PublisherSignatureService.PickTool(binary, ".sig"));
        Assert.AreEqual(PublisherTool.Sigstore, PublisherSignatureService.PickTool(cosign, ".sig"));
        Assert.AreEqual(PublisherTool.Minisign, PublisherSignatureService.PickTool(mini, ".sig"));
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task AFileNamedLikeAnOption_IsRefused_WithNoProcessRun()
    {
        string file = Write("-evil.zip");
        Write("-evil.zip.asc", Pgp);
        FakeProcessRunner runner = new FakeProcessRunner().Answer("gpg", Good());

        PublisherVerification verification = (await new PublisherSignatureService(runner).VerifyAsync(file, null)).Single();

        Assert.AreEqual(PublisherSignatureStatus.Error, verification.Status);
        Assert.AreEqual(0, runner.Calls.Count);
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task AListThatChangesWhileTheToolRuns_IsAnError_NotVerified()
    {
        string file = Write("tool.zip");
        string list = Write("SHA256SUMS", "before");
        Write("SHA256SUMS.asc", Pgp);
        Mock<IExternalProcessRunner> runner = new();
        runner.Setup(r => r.RunAsync("gpg", It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback(() => File.WriteAllText(list, "after"))
            .ReturnsAsync(new ProcessOutput(0, Good(), string.Empty));

        PublisherVerification verification = (await new PublisherSignatureService(runner.Object).VerifyAsync(file, "SHA256SUMS")).Single();

        Assert.AreEqual(PublisherSignatureStatus.Error, verification.Status);
        StringAssert.Contains(verification.Detail, "changed");
    }

    [Timeout(30_000)]
    [TestMethod]
    public async Task ASignatureWrittenOnTheChecksumFile_CountsForThatFile()
    {
        string file = Write("tool.zip");
        Write("tool.zip.sha256", "x");
        Write("tool.zip.sha256.asc", Pgp);
        FakeProcessRunner runner = new FakeProcessRunner().Answer("gpg", Good());

        PublisherVerification verification = (await new PublisherSignatureService(runner).VerifyAsync(file, "tool.zip.sha256")).Single();

        Assert.AreEqual("tool.zip.sha256", verification.Subject);
        Assert.IsNotNull(verification.SubjectSha256);
    }

    [Timeout(30_000)]
    [TestMethod]
    public void ParseWritten_HashesTheVeryBytesItParsed()
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(new string('c', 64) + " *tool.zip\n");

        ExpectedChecksum? written = ChecksumService.ParseWritten(bytes, "tool.zip", "SHA256SUMS");

        Assert.IsNotNull(written);
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(bytes)), written.SourceSha256);
        Assert.IsNull(ChecksumService.ParseWritten(bytes, "other.zip", "SHA256SUMS"));
    }
}

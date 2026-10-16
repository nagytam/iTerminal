using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Signing;
using iTerminal.Core.Test.Helper;
using Moq;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class SignatureDialogTest
{
    private Mock<ISignatureService> _signatures = null!;
    private NotificationService _notifications = null!;
    private SignatureDialog _dialog = null!;

    /// <summary>A panel service with no panels: the dialogs re-read every panel when they finish.</summary>
    private static iConsole.Interfaces.IContentModelService Models()
    {
        var service = new Mock<iConsole.Interfaces.IContentModelService>();
        service.SetupGet(s => s.ContentModels).Returns([]);
        return service.Object;
    }

    [TestInitialize]
    public void Setup()
    {
        _signatures = new Mock<ISignatureService>();
        _signatures.Setup(s => s.GetCapabilitiesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SigningCapabilities(Gpg: true, Authenticode: false));
        _signatures.Setup(s => s.GetCertificatesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _notifications = new NotificationService();
        _dialog = new SignatureDialog(
            _signatures.Object,
            _notifications,
            Models(),
            Mock.Of<IBeepService>(),
            new DateTimeProviderService(),
            UiTestHelper.TestTheme());
    }

    /// <summary>The dialog redraws from its idle pass; a test has to give it one before it reads the screen.</summary>
    private iConsole.Test.Core.ScreenBuffer Screen()
    {
        _dialog.OnIdle();
        return UiTestHelper.Render(_dialog);
    }

    private void Verdicts(params SignatureVerdict[] verdicts) =>
        _signatures.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(verdicts);

    private async Task OpenAsync()
    {
        _dialog.Load(@"C:\data\release.zip");
        await UiTestHelper.ShowAsync(_dialog);
        await _dialog.WorkTask;
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Opening_ChecksTheFile_AndShowsEachMethodsVerdictOnItsOwnLine()
    {
        Verdicts(
            new SignatureVerdict(SignatureMethod.Gpg, SignatureStatus.Valid, "Ada (AAAA)", "Good signature."),
            new SignatureVerdict(SignatureMethod.Pem, SignatureStatus.Invalid, "SHA256:abcd", "BAD signature."));

        await OpenAsync();

        var screen = Screen();
        UiTestHelper.AssertScreenContains(screen, "release.zip");
        UiTestHelper.AssertScreenContains(screen, "✓ OpenPGP: Good signature.");
        UiTestHelper.AssertScreenContains(screen, "by Ada (AAAA)");
        UiTestHelper.AssertScreenContains(screen, "✗ Key signature: BAD signature.");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task NoGpg_SaysWhyOpenPgpSignaturesAreOutOfReach()
    {
        _signatures.Setup(s => s.GetCapabilitiesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SigningCapabilities(Gpg: false, Authenticode: false));
        Verdicts(new SignatureVerdict(SignatureMethod.Gpg, SignatureStatus.NotSigned, string.Empty, "No signature found."));

        await OpenAsync();

        UiTestHelper.AssertScreenContains(Screen(), "gpg is not installed");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task F2_SignsWithGpgUsingTheTypedKey_ThenChecksAgain_AndSaysSo()
    {
        Verdicts(new SignatureVerdict(SignatureMethod.Gpg, SignatureStatus.Valid, "Ada", "Good signature."));
        _signatures.Setup(s => s.SignAsync(@"C:\data\release.zip", SignatureMethod.Gpg, "ada@example.org", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignResult(true, @"C:\data\release.zip.asc", "Wrote release.zip.asc."));
        await OpenAsync();
        _dialog.Key = "ada@example.org";

        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.F2));
        await _dialog.WorkTask;

        _signatures.Verify(s => s.SignAsync(@"C:\data\release.zip", SignatureMethod.Gpg, "ada@example.org", null, It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreEqual("Wrote release.zip.asc.", _dialog.Status);
        Assert.AreEqual(NotificationSeverity.Success, _notifications.History.Single().Severity);
        _signatures.Verify(s => s.VerifyAsync(@"C:\data\release.zip", null, null, It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task F4_SignsWithTheKeyFileTypedInTheBox()
    {
        Verdicts();
        _signatures.Setup(s => s.SignAsync(@"C:\data\release.zip", SignatureMethod.Pem, null, @"C:\keys\me.pem", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignResult(true, @"C:\data\release.zip.sig", "Wrote release.zip.sig."));
        await OpenAsync();
        _dialog.Key = @"C:\keys\me.pem";

        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.F4));
        await _dialog.WorkTask;

        _signatures.Verify(s => s.SignAsync(@"C:\data\release.zip", SignatureMethod.Pem, null, @"C:\keys\me.pem", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task AFailedSigning_ShowsTheReasonAndPostsAnError()
    {
        Verdicts();
        _signatures.Setup(s => s.SignAsync(It.IsAny<string>(), SignatureMethod.Gpg, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignResult(false, null, "gpg: no default secret key"));
        await OpenAsync();

        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.F2));
        await _dialog.WorkTask;

        UiTestHelper.AssertScreenContains(Screen(), "no default secret key");
        Assert.AreEqual(NotificationSeverity.Error, _notifications.History.Single().Severity);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task F5_ChecksAgain_WithTheTypedPublicKey()
    {
        Verdicts(new SignatureVerdict(SignatureMethod.Pem, SignatureStatus.UnknownSigner, string.Empty, "give the signer's public key"));
        await OpenAsync();
        _dialog.Key = @"C:\keys\me.pub.pem";

        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.F5));
        await _dialog.WorkTask;

        _signatures.Verify(s => s.VerifyAsync(@"C:\data\release.zip", null, @"C:\keys\me.pub.pem", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Authenticode_WithSeveralCertificatesAndNoThumbprint_AsksWhichOne()
    {
        _signatures.Setup(s => s.GetCapabilitiesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SigningCapabilities(Gpg: false, Authenticode: true));
        _signatures.Setup(s => s.GetCertificatesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new SigningCertificate("0123456789ABCDEF0123456789ABCDEF01234567", "CN=A", DateTime.UtcNow.AddYears(1)),
            new SigningCertificate("89ABCDEF0123456789ABCDEF0123456789ABCDEF", "CN=B", DateTime.UtcNow.AddYears(1)),
        ]);
        Verdicts();
        await OpenAsync();

        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.F3));
        await _dialog.WorkTask;

        StringAssert.Contains(_dialog.Status, "thumbprint");
        _signatures.Verify(s => s.SignAsync(It.IsAny<string>(), SignatureMethod.Authenticode, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Authenticode_WithOneCertificate_UsesIt()
    {
        _signatures.Setup(s => s.GetCapabilitiesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SigningCapabilities(Gpg: false, Authenticode: true));
        _signatures.Setup(s => s.GetCertificatesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            [new SigningCertificate("0123456789ABCDEF0123456789ABCDEF01234567", "CN=A", DateTime.UtcNow.AddYears(1))]);
        _signatures.Setup(s => s.SignAsync(It.IsAny<string>(), SignatureMethod.Authenticode, "0123456789ABCDEF0123456789ABCDEF01234567", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignResult(true, null, "Signed."));
        Verdicts();
        await OpenAsync();

        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.F3));
        await _dialog.WorkTask;

        _signatures.Verify(s => s.SignAsync(It.IsAny<string>(), SignatureMethod.Authenticode, "0123456789ABCDEF0123456789ABCDEF01234567", null, It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreEqual("Signed.", _dialog.Status);
    }
}

using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Encryption;
using iTerminal.Core.Services.Wipe;
using iTerminal.Core.Test.Helper;
using Moq;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class FileEncryptionDialogTest
{
    private Mock<IFileSystemService> _fileSystem = null!;
    private Mock<ISecureWipeService> _wipe = null!;
    private NotificationService _notifications = null!;
    private FileEncryptionDialog _dialog = null!;
    private string _folder = null!;

    private static iConsole.Interfaces.IContentModelService Models()
    {
        var service = new Mock<iConsole.Interfaces.IContentModelService>();
        service.SetupGet(s => s.ContentModels).Returns([]);
        return service.Object;
    }

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "iTerminalEncDlg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _fileSystem = new Mock<IFileSystemService>();
        _wipe = new Mock<ISecureWipeService>();
        _notifications = new NotificationService();
        var service = new FileEncryptionService(new FileSystemService()) { Iterations = FileEncryptionService.MinIterations, ChunkSize = 1000 };
        _dialog = new FileEncryptionDialog(service, _fileSystem.Object, _wipe.Object, _notifications, Models(),
            Mock.Of<IBeepService>(), new DateTimeProviderService(), UiTestHelper.TestTheme());
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private async Task OpenAsync(params string[] paths)
    {
        _dialog.Load(paths);
        await UiTestHelper.ShowAsync(_dialog);
    }

    private string Write(string name, string content = "hello secret")
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private void Fill(string passphrase, string? confirmation = null)
    {
        _dialog.Passphrase = passphrase;
        _dialog.PassphraseConfirmation = confirmation ?? passphrase;
    }

    private async Task RunAsync()
    {
        _dialog.Start();
        await _dialog.RunTask;
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task Opens_OnEncrypt_NamingTheFile_AndAskingForThePassphraseTwice()
    {
        await OpenAsync(Write("report.txt"));
        _dialog.OnIdle();

        var screen = UiTestHelper.Render(_dialog);
        UiTestHelper.AssertScreenContains(screen, "report.txt");
        UiTestHelper.AssertScreenContains(screen, "Passphrase");
        UiTestHelper.AssertScreenContains(screen, "Confirm");
        Assert.AreEqual(EncryptionMode.Encrypt, _dialog.Mode);
        Assert.IsTrue(_dialog.IsConfirmEnabledForTest);
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task Opens_OnDecrypt_WhenEveryFileIsDotEnc_WithoutAConfirmation()
    {
        await OpenAsync(Write("a.txt.enc"), Write("b.txt.enc"));

        Assert.AreEqual(EncryptionMode.Decrypt, _dialog.Mode);
        Assert.IsFalse(_dialog.IsConfirmEnabledForTest);
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task Typed_PassphraseIsNeverDrawn()
    {
        await OpenAsync(Write("report.txt"));
        Fill("hunter2-visible?");
        _dialog.OnIdle();

        UiTestHelper.AssertScreenDoesNotContain(UiTestHelper.Render(_dialog), "hunter2");
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task Start_RefusesAnEmptyPassphrase_AndAMismatch_AndStartsNothing()
    {
        var path = Write("report.txt");
        await OpenAsync(path);

        _dialog.Start();
        Assert.AreEqual("Type a passphrase.", _dialog.ErrorMessage.Text);

        Fill("one", "two");
        _dialog.Start();
        Assert.AreEqual("The passphrases do not match.", _dialog.ErrorMessage.Text);
        Assert.AreEqual(EncryptionPhase.Entering, _dialog.Phase);
        Assert.IsFalse(File.Exists(path + ".enc"));
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task Run_Encrypts_ClearsThePassphrase_AndAsksAboutTheOriginal()
    {
        var path = Write("report.txt");
        await OpenAsync(path);
        Fill("s3cret passphrase");

        await RunAsync();
        _dialog.OnIdle();

        Assert.IsTrue(File.Exists(path + ".enc"));
        Assert.IsTrue(File.Exists(path), "the original stays until it is asked about");
        Assert.AreEqual(string.Empty, _dialog.Passphrase);
        Assert.AreEqual(string.Empty, _dialog.PassphraseConfirmation);
        Assert.AreEqual(EncryptionPhase.AskDelete, _dialog.Phase);
        StringAssert.Contains(_dialog.StatusForTest, "y/n");
        _fileSystem.Verify(f => f.DeleteFile(It.IsAny<Url>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task AnsweringN_KeepsTheOriginal_AndNothingIsDeleted()
    {
        var path = Write("report.txt");
        await OpenAsync(path);
        Fill("s3cret passphrase");
        await RunAsync();

        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.N, 'n'));
        _dialog.OnIdle();

        Assert.AreEqual(EncryptionPhase.Finished, _dialog.Phase);
        Assert.IsTrue(File.Exists(path));
        StringAssert.Contains(_dialog.StatusForTest, "kept");
        _fileSystem.Verify(f => f.DeleteFile(It.IsAny<Url>(), It.IsAny<bool>()), Times.Never);
        _wipe.Verify(w => w.WipeAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<WipePattern>(), It.IsAny<IProgress<WipeProgress>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task AnsweringY_SendsOnlyTheEncryptedOriginals_ToTheRecycleBin()
    {
        var done = Write("done.txt");
        var existing = Write("blocked.txt");
        Write("blocked.txt.enc", "already here");
        await OpenAsync(done, existing);
        Fill("s3cret passphrase");
        await RunAsync();
        _fileSystem.Setup(f => f.DeleteFile(It.IsAny<Url>(), true)).Returns(true);

        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.Y, 'y'));
        await _dialog.DeleteTask;

        _fileSystem.Verify(f => f.DeleteFile(It.Is<Url>(u => u.FullName == done), true), Times.Once);
        _fileSystem.Verify(f => f.DeleteFile(It.Is<Url>(u => u.FullName == existing), It.IsAny<bool>()), Times.Never, "its output was refused, so its original must stay");
        Assert.AreEqual(EncryptionPhase.Finished, _dialog.Phase);
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task AFailedRun_DoesNotOfferToDeleteAnything()
    {
        var path = Write("report.txt");
        Write("report.txt.enc", "already here");
        await OpenAsync(path);
        Fill("s3cret passphrase");

        await RunAsync();
        _dialog.OnIdle();

        Assert.AreEqual(EncryptionPhase.Finished, _dialog.Phase);
        StringAssert.Contains(_dialog.ErrorMessage.Text, "already exists");
        await _dialog.HandleKeyEventAsync(UiTestHelper.Key(ConsoleKey.Y, 'y'));
        _fileSystem.Verify(f => f.DeleteFile(It.IsAny<Url>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task Decrypt_WithTheWrongPassphrase_SaysSo_AndChangesNothing()
    {
        var path = Write("report.txt");
        await OpenAsync(path);
        Fill("right one");
        await RunAsync();
        File.Delete(path);

        _dialog.Load([path + ".enc"]);
        Fill("wrong one");
        await RunAsync();
        _dialog.OnIdle();

        Assert.IsFalse(File.Exists(path));
        Assert.AreEqual(EncryptionPhase.Finished, _dialog.Phase);
        StringAssert.Contains(_dialog.ErrorMessage.Text, "Wrong passphrase");
        Assert.AreEqual(1, Directory.GetFiles(_folder).Length, "only the .enc is left");
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task Close_CancelsAndClearsTheBoxes()
    {
        await OpenAsync(Write("report.txt"));
        Fill("typed but not run");

        _dialog.Close();

        Assert.AreEqual(string.Empty, _dialog.Passphrase);
        Assert.AreEqual(string.Empty, _dialog.PassphraseConfirmation);
    }
}

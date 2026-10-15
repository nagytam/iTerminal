using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Test.Helper;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>Alt+F5: the archive's name and format follow each other, and each format enables only what it honours.</summary>
[TestClass]
public class CompressDialogTest
{
    private string _root = string.Empty;
    private string Target => Path.Combine(_root, "dst");

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "CompressDialogTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Target);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private static IArchiveCompressionService Service(bool sevenZip = true) =>
        new ArchiveCompressionService(new FakeProcessRunner(), new SevenZipArchiveWriter(new FakeProcessRunner(), () => sevenZip ? "7z" : null));

    private (CompressDialog Dialog, MarkableTestModel Model) Create(bool sevenZip = true)
    {
        var model = MarkableTestModel.Create(Path.Combine(_root, "src"));
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetFocusedModel()).Returns(model);
        service.Setup(s => s.GetLastContentModel()).Returns(model);
        service.SetupGet(s => s.NonFocusedFolderUrl).Returns(new Url(Target + Path.DirectorySeparatorChar));
        var dialog = new CompressDialog(service.Object, Service(sevenZip), null!, null!, Mock.Of<IBeepService>(),
            new DateTimeProviderService(), TestTheme(), null!, null!, null!, null!)
        {
            SourceModel = model,
        };
        return (dialog, model);
    }

    [TestMethod]
    public async Task OneFileIsPackedIntoAZipNamedAfterItInTheOtherPanel()
    {
        var (dialog, model) = Create();
        model.CursorPosition.Y = 2; // a.txt

        await ShowAsync(dialog);

        Assert.AreEqual("Compress a.txt", dialog.Title);
        Assert.AreEqual(ArchiveFormat.Zip, dialog.Format);
        StringAssert.EndsWith(dialog.ArchivePath, Path.Combine("dst", "a.zip"));
        Assert.AreEqual(6, dialog.Level);
        AssertScreenContains(Render(dialog), "Password");
    }

    [TestMethod]
    public async Task SeveralMarkedRowsArePackedIntoAnArchiveNamedAfterTheirFolder()
    {
        var (dialog, model) = Create();
        model.SetMarks("*", marked: true, includeFolders: true);

        await ShowAsync(dialog);

        Assert.AreEqual("Compress 4 items", dialog.Title);
        StringAssert.EndsWith(dialog.ArchivePath, Path.Combine("dst", "src.zip"));
        StringAssert.Contains(dialog.SourceLabelForTest, "3 files, 1 folder");
    }

    [TestMethod]
    public async Task ChoosingAFormatChangesTheEndingAndTheFields()
    {
        var (dialog, model) = Create();
        model.CursorPosition.Y = 2;
        await ShowAsync(dialog);

        dialog.Format = ArchiveFormat.TarGz;

        StringAssert.EndsWith(dialog.ArchivePath, "a.tar.gz");
        Assert.AreEqual(6, dialog.Level);
        Assert.IsTrue(dialog.IsLevelEnabledForTest);
        Assert.IsFalse(dialog.IsPasswordEnabledForTest, "TAR.GZ takes no password");
        StringAssert.Contains(dialog.HintForTest, "takes no password");

        dialog.Format = ArchiveFormat.Tar;
        StringAssert.EndsWith(dialog.ArchivePath, "a.tar");
        Assert.IsFalse(dialog.IsLevelEnabledForTest, "TAR does not compress");
    }

    [TestMethod]
    public async Task TypingAnEndingChoosesTheFormat()
    {
        var (dialog, model) = Create();
        model.CursorPosition.Y = 2;
        await ShowAsync(dialog);

        dialog.ArchivePath = Path.Combine(Target, "backup.7z");

        Assert.AreEqual(ArchiveFormat.SevenZip, dialog.Format);
        Assert.AreEqual(5, dialog.Level);
        StringAssert.EndsWith(dialog.ArchivePath, "backup.7z", "choosing it did not rename what was typed");
    }

    [TestMethod]
    public async Task ThePasswordOptionsFollowThePassword()
    {
        var (dialog, model) = Create();
        model.CursorPosition.Y = 2;
        await ShowAsync(dialog);

        Assert.IsFalse(dialog.IsEncryptionEnabledForTest, "no password, nothing to encrypt with");
        dialog.Password = "s3cret";
        Assert.IsTrue(dialog.IsEncryptionEnabledForTest, "ZIP: AES-256 or ZipCrypto");
        Assert.IsFalse(dialog.IsEncryptNamesEnabledForTest, "ZIP cannot hide its names");

        dialog.Format = ArchiveFormat.SevenZip;
        Assert.IsFalse(dialog.IsEncryptionEnabledForTest, "7z has only AES-256");
        Assert.IsTrue(dialog.IsEncryptNamesEnabledForTest);
    }

    [TestMethod]
    public async Task MismatchedPasswordsAreRefused()
    {
        var (dialog, model) = Create();
        model.CursorPosition.Y = 2;
        await ShowAsync(dialog);
        dialog.Password = "one";
        dialog.PasswordConfirmation = "two";

        var (options, error) = dialog.ReadOptions();

        Assert.IsNull(options);
        Assert.AreEqual("The passwords do not match.", error);
    }

    [TestMethod]
    public async Task TheOptionsAreWhatTheFieldsSay()
    {
        var (dialog, model) = Create();
        model.CursorPosition.Y = 2;
        await ShowAsync(dialog);
        dialog.Format = ArchiveFormat.SevenZip;
        dialog.Level = 9;
        dialog.Password = dialog.PasswordConfirmation = "s3cret";
        dialog.EncryptNames = true;
        dialog.ReplaceExisting = false;

        var (options, _) = dialog.ReadOptions();

        Assert.AreEqual(new CompressionOptions(ArchiveFormat.SevenZip, 9, "s3cret", ArchiveEncryption.Aes256, true, false), options);
    }

    [TestMethod]
    public async Task WithoutSevenZipThe7zFormatIsNotListedAndTheHintSaysWhy()
    {
        var (dialog, model) = Create(sevenZip: false);
        model.CursorPosition.Y = 2;
        await ShowAsync(dialog);

        dialog.ArchivePath = Path.Combine(Target, "backup.7z");

        Assert.AreEqual(ArchiveFormat.Zip, dialog.Format);
        StringAssert.Contains(dialog.HintForTest, "7z needs 7-Zip installed");
        StringAssert.Contains(dialog.ReadOptions().Error, "ends in .7z, but the format is ZIP");
    }

    [TestMethod]
    public async Task AnExistingArchiveIsSaidToBeAddedTo()
    {
        var (dialog, model) = Create();
        model.CursorPosition.Y = 2;
        File.WriteAllText(Path.Combine(Target, "a.zip"), "pretend");

        await ShowAsync(dialog);

        StringAssert.StartsWith(dialog.HintForTest, "Adds to the existing archive.");
    }

    [TestMethod]
    public void AltF5OpensItOnlyWhenThereIsSomethingToPack()
    {
        var (dialog, model) = Create();
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetFocusedModel()).Returns(model);
        var command = new CompressDialogCommand(dialog, service.Object);

        Assert.AreEqual(ConsoleKey.F5, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Alt);
        Assert.IsFalse(command.CanExecute(), "the cursor is on ..");
        model.CursorPosition.Y = 2;
        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    [DataRow("x.zip", "ZIP")]
    [DataRow("x.TAR.GZ", "TAR.GZ")]
    [DataRow("x.tgz", "TAR.GZ")]
    [DataRow("x.tar", "TAR")]
    [DataRow("x.tbz2", "TAR.BZ2")]
    [DataRow("x.7z", "7z")]
    [DataRow("x.rar", null)]
    public void TheFormatIsReadFromTheName(string name, string? format)
    {
        Assert.AreEqual(format, ArchiveFormat.FromFileName(name)?.Name);
    }

    [TestMethod]
    public void ApplyingAnEndingReplacesTheWholeOldOne()
    {
        Assert.AreEqual("backup.zip", ArchiveFormat.Zip.ApplyExtension("backup.tar.gz"));
        Assert.AreEqual("backup.tar.bz2", ArchiveFormat.TarBz2.ApplyExtension("backup"));
    }
}

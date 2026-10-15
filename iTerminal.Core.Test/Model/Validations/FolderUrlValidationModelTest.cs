using iConsole.Data;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Model.Validations;
using Moq;

namespace iTerminal.Core.Test.Model.Validations;

/// <summary>
/// The validator behind the folder selector's Path box. It runs off a keystroke, so what it must
/// never do is go near the network to decide whether a remote URL is worth accepting.
/// </summary>
[TestClass]
public class FolderUrlValidationModelTest
{
    private static FolderUrlValidationModel Validate(string text)
    {
        var dateTimeProviderService = new Mock<IDateTimeProviderService>();
        dateTimeProviderService.SetupGet(m => m.Now).Returns(DateTime.Now);
        return new FolderUrlValidationModel(new TextModel(text), dateTimeProviderService.Object);
    }

    [TestMethod]
    public void EmptyText_IsInvalidAndSaysWhat()
    {
        var validationModel = Validate(string.Empty);

        Assert.IsFalse(validationModel.IsValid);
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(validationModel.ErrorMessage, "path");
    }

    [TestMethod]
    [DataRow("file://C:/Windows")]
    [DataRow("ssh://tamas@example.com:/home/")]
    [DataRow("ftp://example.com/pub/")]
    [DataRow("smb://server/share/")]
    [DataRow("iphone://0123456789abcdef/DCIM/")]
    [DataRow("memdrive://")]
    [DataRow("musiccatalog://")]
    [DataRow("musicplaylist://")]
    [DataRow("zip://C:/a.zip@/")]
    [DataRow("gz://C:/a.gz@/")]
    [DataRow("targz://C:/a.tar.gz@/")]
    public void AKnownScheme_IsAcceptedWithoutTouchingAnything(string url)
    {
        var validationModel = Validate(url);

        Assert.IsTrue(validationModel.IsValid, $"{url} should be accepted on sight.");
        Assert.AreEqual(string.Empty, validationModel.ErrorMessage);
    }

    [TestMethod]
    public void AKnownScheme_IsRecognisedRegardlessOfCase()
    {
        Assert.IsTrue(Validate("SSH://tamas@example.com:/home/").IsValid);
    }

    [TestMethod]
    public void AnUnknownScheme_IsRejectedAndPointsAtTheList()
    {
        var validationModel = Validate("gopher://example.com/");

        Assert.IsFalse(validationModel.IsValid);
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(validationModel.ErrorMessage, "scheme");
    }

    [TestMethod]
    public void AnExistingLocalFolder_IsValid()
    {
        Assert.IsTrue(Validate(Path.GetTempPath()).IsValid);
    }

    [TestMethod]
    public void ALocalFolderThatIsNotThere_IsInvalid()
    {
        var validationModel = Validate(Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid()));

        Assert.IsFalse(validationModel.IsValid);
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(validationModel.ErrorMessage, "No such folder");
    }

    [TestMethod]
    public void SurroundingSpace_IsIgnored()
    {
        Assert.IsTrue(Validate("  " + Path.GetTempPath() + "  ").IsValid);
    }

    [TestMethod]
    public void SupportedSchemes_CoverEverySchemeTheAppDeclares()
    {
        // Container is the app's own list of drive kinds; a scheme added there and not here is one
        // the Path box tells the user does not exist.
        string[] declaredSchemes =
        [
            Container.FILE, Container.ZIP_ARCHIVE, Container.GZ_ARCHIVE, Container.TAR_GZ_ARCHIVE,
            Container.SSH, Container.FTP, Container.SMB, Container.IPHONE, Container.MEMORY_DRIVE,
            Container.MUSIC_CATALOG, Container.MUSIC_PLAYLIST, Container.DOCKER, Container.GIT,
            Container.SEARCH_RESULTS, Container.PREVIEW, Container.ARCHIVE, Container.RECYCLE,
        ];

        CollectionAssert.AreEquivalent(declaredSchemes, FolderUrlValidationModel.SupportedSchemes);
    }

    [TestMethod]
    public void HasSupportedScheme_IsWhatTheHintLineAndTheValidatorBothAskDirectly()
    {
        Assert.IsTrue(FolderUrlValidationModel.HasSupportedScheme("smb://server/share/"));
        Assert.IsFalse(FolderUrlValidationModel.HasSupportedScheme(@"C:\Windows"));
    }

    [TestMethod]
    public void NoModel_IsValid()
    {
        var dateTimeProviderService = new Mock<IDateTimeProviderService>();
        var validationModel = new FolderUrlValidationModel(null!, dateTimeProviderService.Object);

        Assert.IsTrue(validationModel.IsValid);
    }
}

using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Model.ContentModels.Test;

[TestClass]
public class ContentModelSmbDriveTest
{
    private static ContentModelSmbDrive CreateModel(
        Mock<ISmbFileSystemService> smbFileSystemService,
        INetworkProfileService? networkProfileService = null,
        IPasswordManager? passwordManager = null)
    {
        var settingsService = new Mock<ISettingsService>(MockBehavior.Loose);
        if (networkProfileService is null)
        {
            var emptyProfileService = new Mock<INetworkProfileService>();
            emptyProfileService.Setup(s => s.GetAll()).Returns([]);
            networkProfileService = emptyProfileService.Object;
        }
        passwordManager ??= Mock.Of<IPasswordManager>();
        return new ContentModelSmbDrive(
            name: "TestPanel",
            settingsService: settingsService.Object,
            smbFileSystemService: smbFileSystemService.Object,
            networkProfileService: networkProfileService,
            passwordManager: passwordManager,
            folderHistoryService: new FolderHistoryService(settingsService.Object),
            frequentExecutionGuardService: Mock.Of<IFrequentExecutionGuardService>(),
            memoryCacheService: Mock.Of<IMemoryCacheService>(),
            logger: Mock.Of<Serilog.ILogger>());
    }

    [TestMethod]
    public void IsReadOnly_ReturnsFalse()
    {
        var model = CreateModel(new Mock<ISmbFileSystemService>());
        Assert.IsFalse(model.IsReadOnly);
    }

    [TestMethod]
    public void SetUrl_WithoutTrailingSlash_ShowsAFolder()
    {
        var model = CreateModel(new Mock<ISmbFileSystemService>());

        model.SetUrl(new Url("smb://user@host/Share/My Documents"));

        Assert.AreEqual("smb://user@host/Share/My Documents/", model.Url.FullUrl);
        Assert.AreEqual(PathType.Folder, model.Url.PathType);
    }

    [TestMethod]
    public void ListChildren_LeavesOutTheParentRow()
    {
        var smbFileSystemService = new Mock<ISmbFileSystemService>();
        smbFileSystemService.Setup(s => s.List(It.IsAny<Url>())).Returns(
        [
            new FileSystemEntry("..", "smb://host/Share/"),
            new FileSystemEntry("a b.txt", "smb://host/Share/dir/a b.txt"),
        ]);
        var model = CreateModel(smbFileSystemService);

        var children = model.ListChildren(new Url("smb://host/Share/dir/")).ToList();

        Assert.AreEqual(1, children.Count);
        Assert.AreEqual("smb://host/Share/dir/a b.txt", children[0].FullUrl);
    }

    [TestMethod]
    public void SetUrl_NoMatchingProfile_ConnectsAnonymously()
    {
        var smbFileSystemService = new Mock<ISmbFileSystemService>();
        var model = CreateModel(smbFileSystemService);

        model.SetUrl(new Url("smb://user@host/Share/"));

        smbFileSystemService.Verify(s => s.Connect("host", "Share", "user", string.Empty), Times.Once);
    }

    [TestMethod]
    public void SetUrl_MatchingProfileWithSavedCredential_ConnectsWithPasswordFromVault()
    {
        var smbFileSystemService = new Mock<ISmbFileSystemService>();
        var profile = new NetworkConnectionProfile
        {
            Protocol = NetworkProtocol.Smb,
            Host = "host",
            ShareName = "Share",
            Username = "user",
            HasSavedCredential = true,
        };
        var networkProfileService = new Mock<INetworkProfileService>();
        networkProfileService.Setup(s => s.GetAll()).Returns([profile]);
        var passwordManager = new Mock<IPasswordManager>();
        passwordManager.Setup(v => v.GetSecret(profile.Id)).Returns("hunter2");
        var model = CreateModel(smbFileSystemService, networkProfileService.Object, passwordManager.Object);

        model.SetUrl(new Url("smb://user@host/Share/"));

        smbFileSystemService.Verify(s => s.Connect("host", "Share", "user", "hunter2"), Times.Once);
    }

    [TestMethod]
    public async Task MoveAsync_DelegatesToSmbFileSystemService()
    {
        var smbFileSystemService = new Mock<ISmbFileSystemService>();
        var model = CreateModel(smbFileSystemService);
        var sourceUrl = new Url("smb://user@host/Share/a.txt");
        var targetUrl = new Url("smb://user@host/Share/b.txt");

        await model.MoveAsync(sourceUrl, targetUrl, true);

        smbFileSystemService.Verify(s => s.MoveAsync(sourceUrl, targetUrl, true), Times.Once);
    }

    [TestMethod]
    public async Task CopyAsync_DelegatesToSmbFileSystemService()
    {
        var smbFileSystemService = new Mock<ISmbFileSystemService>();
        var model = CreateModel(smbFileSystemService);
        var sourceUrl = new Url("smb://user@host/Share/a.txt");
        var targetUrl = new Url("smb://user@host/Share/b.txt");

        await model.CopyAsync(sourceUrl, targetUrl, false);

        smbFileSystemService.Verify(s => s.CopyAsync(sourceUrl, targetUrl, false), Times.Once);
    }

    [TestMethod]
    public void ExistsFile_DelegatesToSmbFileSystemService()
    {
        var smbFileSystemService = new Mock<ISmbFileSystemService>();
        var url = new Url("smb://user@host/Share/a.txt");
        smbFileSystemService.Setup(s => s.ExistsFile(url)).Returns(true);
        var model = CreateModel(smbFileSystemService);

        Assert.IsTrue(model.ExistsFile(url));
    }
}

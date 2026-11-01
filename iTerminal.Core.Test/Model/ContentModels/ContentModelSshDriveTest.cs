using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Model.ContentModels.Test;

[TestClass]
public class ContentModelSshDriveTest
{
    private static ContentModelSshDrive CreateModel(
        Mock<ISshFileSystemService> sshFileSystemService,
        INetworkProfileService? networkProfileService = null,
        IPasswordManager? passwordManager = null)
    {
        var settingsService = new Mock<ISettingsService>(MockBehavior.Loose);
        return new ContentModelSshDrive(
            name: "TestPanel",
            settingsService: settingsService.Object,
            sshFileSystemService: sshFileSystemService.Object,
            folderHistoryService: new FolderHistoryService(settingsService.Object),
            frequentExecutionGuardService: Mock.Of<IFrequentExecutionGuardService>(),
            memoryCacheService: Mock.Of<IMemoryCacheService>(),
            logger: Mock.Of<Serilog.ILogger>(),
            networkProfileService: networkProfileService,
            passwordManager: passwordManager);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsReadOnly_ReturnsFalse()
    {
        var model = CreateModel(new Mock<ISshFileSystemService>());
        Assert.IsFalse(model.IsReadOnly);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MoveAsync_DelegatesToSshFileSystemService()
    {
        var sshFileSystemService = new Mock<ISshFileSystemService>();
        var model = CreateModel(sshFileSystemService);
        var sourceUrl = new Url("ssh://user@host:/a.txt");
        var targetUrl = new Url("ssh://user@host:/b.txt");

        await model.MoveAsync(sourceUrl, targetUrl, true);

        sshFileSystemService.Verify(s => s.MoveAsync(sourceUrl, targetUrl, true), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CopyAsync_DelegatesToSshFileSystemService()
    {
        var sshFileSystemService = new Mock<ISshFileSystemService>();
        var model = CreateModel(sshFileSystemService);
        var sourceUrl = new Url("ssh://user@host:/a.txt");
        var targetUrl = new Url("ssh://user@host:/b.txt");

        await model.CopyAsync(sourceUrl, targetUrl, false);

        sshFileSystemService.Verify(s => s.CopyAsync(sourceUrl, targetUrl, false), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetUrl_NoMatchingProfile_ConnectsWithPortableDefaultKeyPathAndNoPassword()
    {
        var sshFileSystemService = new Mock<ISshFileSystemService>();
        var networkProfileService = new Mock<INetworkProfileService>();
        networkProfileService.Setup(s => s.GetAll()).Returns([]);
        var model = CreateModel(sshFileSystemService, networkProfileService.Object, Mock.Of<IPasswordManager>());

        model.SetUrl(new Url("ssh://user@host:/"));

        sshFileSystemService.Verify(s => s.Connect(
            "host", "user", null,
            It.Is<string>(path => path.EndsWith(".ssh" + Path.DirectorySeparatorChar + "id_rsa"))),
            Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetUrl_MatchingProfileWithSavedPassword_ConnectsWithPasswordFromVault()
    {
        var sshFileSystemService = new Mock<ISshFileSystemService>();
        var profile = new NetworkConnectionProfile
        {
            Protocol = NetworkProtocol.Ssh,
            Host = "host",
            Username = "user",
            SshAuthMethod = SshAuthMethod.Password,
            HasSavedCredential = true,
        };
        var networkProfileService = new Mock<INetworkProfileService>();
        networkProfileService.Setup(s => s.GetAll()).Returns([profile]);
        var passwordManager = new Mock<IPasswordManager>();
        passwordManager.Setup(v => v.GetSecret(profile.Id)).Returns("hunter2");
        var model = CreateModel(sshFileSystemService, networkProfileService.Object, passwordManager.Object);

        model.SetUrl(new Url("ssh://user@host:/"));

        sshFileSystemService.Verify(s => s.Connect("host", "user", "hunter2", null), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Refresh_ListingShrinks_RemovesAllTrailingEntries()
    {
        var sshFileSystemService = new Mock<ISshFileSystemService>();
        var fullListing = new[]
        {
            CreateEntry("ssh://user@host:/a.txt"),
            CreateEntry("ssh://user@host:/b.txt"),
            CreateEntry("ssh://user@host:/c.txt"),
        };
        var shrunkListing = new[] { fullListing[0] };
        sshFileSystemService.SetupSequence(s => s.List(It.IsAny<Url>()))
            .Returns(fullListing)
            .Returns(shrunkListing);
        var model = CreateModel(sshFileSystemService);

        model.SetUrl(new Url("ssh://user@host:/"));
        Assert.AreEqual(3, model.Entries.Count);

        model.Refresh();

        Assert.AreEqual(1, model.Entries.Count);
        Assert.AreEqual(fullListing[0].Url.FullUrl, model.Entries[0].Url.FullUrl);
    }

    private static IEntry CreateEntry(string url) => Mock.Of<IEntry>(e => e.Url == new Url(url));

    [Timeout(60_000)]
    [TestMethod]
    public void SetUrl_MatchingProfileWithPrivateKeyPath_ConnectsWithThatKeyPath()
    {
        var sshFileSystemService = new Mock<ISshFileSystemService>();
        var profile = new NetworkConnectionProfile
        {
            Protocol = NetworkProtocol.Ssh,
            Host = "host",
            Username = "user",
            SshAuthMethod = SshAuthMethod.PrivateKey,
            PrivateKeyPath = "/home/user/.ssh/id_ed25519",
        };
        var networkProfileService = new Mock<INetworkProfileService>();
        networkProfileService.Setup(s => s.GetAll()).Returns([profile]);
        var model = CreateModel(sshFileSystemService, networkProfileService.Object, Mock.Of<IPasswordManager>());

        model.SetUrl(new Url("ssh://user@host:/"));

        sshFileSystemService.Verify(s => s.Connect("host", "user", null, "/home/user/.ssh/id_ed25519"), Times.Once);
    }
}

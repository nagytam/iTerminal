using iConsole.Data;
using iTerminal.Core.Services;
using Moq;
using SMBLibrary;
using SMBLibrary.Client;
using FileAttributes = SMBLibrary.FileAttributes;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class SmbFileSystemServiceTest
{
    private static Mock<ISMBClient> CreateConnectedClientMock(Mock<ISMBFileStore>? fileStore = null)
    {
        var client = new Mock<ISMBClient>();
        client.Setup(c => c.Connect(It.IsAny<string>(), It.IsAny<SMBTransportType>())).Returns(true);
        client.Setup(c => c.Login(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(NTStatus.STATUS_SUCCESS);
        client.SetupGet(c => c.IsConnected).Returns(true);
        client.SetupGet(c => c.MaxReadSize).Returns(65536u);
        client.SetupGet(c => c.MaxWriteSize).Returns(65536u);
        var store = fileStore ?? new Mock<ISMBFileStore>();
        client.Setup(c => c.TreeConnect(It.IsAny<string>(), out It.Ref<NTStatus>.IsAny))
            .Returns(new TreeConnectDelegate((string share, out NTStatus status) =>
            {
                status = NTStatus.STATUS_SUCCESS;
                return store.Object;
            }));
        return client;
    }

    private delegate ISMBFileStore TreeConnectDelegate(string shareName, out NTStatus status);

    private static SmbFileSystemService CreateConnectedService(Mock<ISMBFileStore>? fileStore = null, Mock<ISMBClient>? client = null)
    {
        client ??= CreateConnectedClientMock(fileStore);
        var service = new SmbFileSystemService(() => client.Object);
        service.Connect("host", "Share", "user", "pw");
        return service;
    }

    private static void SetupCreateFileSuccess(Mock<ISMBFileStore> fileStore, object handle = null!)
    {
        handle ??= new object();
        var fileStatus = FileStatus.FILE_OPENED;
        fileStore.Setup(fs => fs.CreateFile(
                out handle, out fileStatus,
                It.IsAny<string>(), It.IsAny<AccessMask>(), It.IsAny<FileAttributes>(),
                It.IsAny<ShareAccess>(), It.IsAny<CreateDisposition>(), It.IsAny<CreateOptions>(), null))
            .Returns(NTStatus.STATUS_SUCCESS);
    }

    private static void SetupCreateFileFailure(Mock<ISMBFileStore> fileStore)
    {
        object handle = null!;
        var fileStatus = FileStatus.FILE_DOES_NOT_EXIST;
        fileStore.Setup(fs => fs.CreateFile(
                out handle, out fileStatus,
                It.IsAny<string>(), It.IsAny<AccessMask>(), It.IsAny<FileAttributes>(),
                It.IsAny<ShareAccess>(), It.IsAny<CreateDisposition>(), It.IsAny<CreateOptions>(), null))
            .Returns(NTStatus.STATUS_OBJECT_NAME_NOT_FOUND);
    }

    // ── ListShares ───────────────────────────────────────────────────────────

    [TestMethod]
    public void ListShares_Success_ReturnsShares()
    {
        var client = new Mock<ISMBClient>();
        client.Setup(c => c.Connect(It.IsAny<string>(), It.IsAny<SMBTransportType>())).Returns(true);
        client.Setup(c => c.Login(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(NTStatus.STATUS_SUCCESS);
        var listStatus = NTStatus.STATUS_SUCCESS;
        client.Setup(c => c.ListShares(out listStatus)).Returns(["Public", "Users"]);
        var service = new SmbFileSystemService(() => client.Object);

        var shares = service.ListShares("host", "user", "pw").ToList();

        CollectionAssert.AreEqual(new List<string> { "Public", "Users" }, shares);
        client.Verify(c => c.Disconnect(), Times.Once);
    }

    [TestMethod]
    public void ListShares_ConnectFails_ReturnsEmpty()
    {
        var client = new Mock<ISMBClient>();
        client.Setup(c => c.Connect(It.IsAny<string>(), It.IsAny<SMBTransportType>())).Returns(false);
        var service = new SmbFileSystemService(() => client.Object);

        var shares = service.ListShares("host", "user", "pw");

        Assert.AreEqual(0, shares.Count());
    }

    [TestMethod]
    public void ListShares_LoginFails_ReturnsEmpty()
    {
        var client = new Mock<ISMBClient>();
        client.Setup(c => c.Connect(It.IsAny<string>(), It.IsAny<SMBTransportType>())).Returns(true);
        client.Setup(c => c.Login(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(NTStatus.STATUS_LOGON_FAILURE);
        var service = new SmbFileSystemService(() => client.Object);

        var shares = service.ListShares("host", "user", "wrong-pw");

        Assert.AreEqual(0, shares.Count());
    }

    // ── Connect ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Connect_Success_TreeConnectsToShare()
    {
        var client = CreateConnectedClientMock();

        var service = new SmbFileSystemService(() => client.Object);
        service.Connect("host", "Share", "user", "pw");

        client.Verify(c => c.TreeConnect("Share", out It.Ref<NTStatus>.IsAny), Times.Once);
        Assert.IsTrue(service.IsConnected);
    }

    [TestMethod]
    public void Connect_TransportConnectFails_Throws()
    {
        var client = new Mock<ISMBClient>();
        client.Setup(c => c.Connect(It.IsAny<string>(), It.IsAny<SMBTransportType>())).Returns(false);
        var service = new SmbFileSystemService(() => client.Object);

        Assert.ThrowsExactly<IOException>(() => service.Connect("host", "Share", "user", "pw"));
    }

    [TestMethod]
    public void Connect_LoginFails_Throws()
    {
        var client = new Mock<ISMBClient>();
        client.Setup(c => c.Connect(It.IsAny<string>(), It.IsAny<SMBTransportType>())).Returns(true);
        client.Setup(c => c.Login(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(NTStatus.STATUS_LOGON_FAILURE);
        var service = new SmbFileSystemService(() => client.Object);

        Assert.ThrowsExactly<IOException>(() => service.Connect("host", "Share", "user", "wrong-pw"));
    }

    // ── List ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void List_MapsDirectoryEntries_SkippingDotAndDotDot()
    {
        var fileStore = new Mock<ISMBFileStore>();
        SetupCreateFileSuccess(fileStore);
        var dirList = new List<QueryDirectoryFileInformation>
        {
            new FileDirectoryInformation { FileName = ".", FileAttributes = FileAttributes.Directory },
            new FileDirectoryInformation { FileName = "..", FileAttributes = FileAttributes.Directory },
            new FileDirectoryInformation { FileName = "docs", FileAttributes = FileAttributes.Directory, LastWriteTime = new DateTime(2026, 1, 2) },
            new FileDirectoryInformation { FileName = "readme.txt", FileAttributes = FileAttributes.Normal, EndOfFile = 42, LastWriteTime = new DateTime(2026, 1, 3) },
        };
        fileStore.Setup(fs => fs.QueryDirectory(out dirList, It.IsAny<object>(), "*", FileInformationClass.FileDirectoryInformation))
            .Returns(NTStatus.STATUS_SUCCESS);

        var service = CreateConnectedService(fileStore);
        var entries = service.List(new Url("smb://user@host/Share/sub/")).ToList();

        var names = entries.Select(e => e.DisplayName).ToList();
        CollectionAssert.Contains(names, "..");
        CollectionAssert.Contains(names, "docs");
        CollectionAssert.Contains(names, "readme.txt");
        Assert.AreEqual(3, entries.Count);
    }

    [TestMethod]
    public void List_AtShareRoot_DoesNotIncludeParentEntry()
    {
        var fileStore = new Mock<ISMBFileStore>();
        SetupCreateFileSuccess(fileStore);
        var dirList = new List<QueryDirectoryFileInformation>();
        fileStore.Setup(fs => fs.QueryDirectory(out dirList, It.IsAny<object>(), "*", FileInformationClass.FileDirectoryInformation))
            .Returns(NTStatus.STATUS_NO_MORE_FILES);

        var service = CreateConnectedService(fileStore);
        var entries = service.List(new Url("smb://user@host/Share/")).ToList();

        Assert.AreEqual(0, entries.Count);
    }

    [TestMethod]
    public void List_CreateFileFails_ReturnsOnlyParentEntry()
    {
        // The ".." entry is added before attempting to open the (unreachable) directory, so
        // the user can still navigate back out of a folder that failed to open.
        var fileStore = new Mock<ISMBFileStore>();
        SetupCreateFileFailure(fileStore);

        var service = CreateConnectedService(fileStore);
        var entries = service.List(new Url("smb://user@host/Share/missing/")).ToList();

        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("..", entries[0].DisplayName);
    }

    // ── Exists ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void ExistsFile_CreateFileSucceeds_ReturnsTrue()
    {
        var fileStore = new Mock<ISMBFileStore>();
        SetupCreateFileSuccess(fileStore);
        var service = CreateConnectedService(fileStore);

        Assert.IsTrue(service.ExistsFile(new Url("smb://user@host/Share/a.txt")));
    }

    [TestMethod]
    public void ExistsFile_CreateFileFails_ReturnsFalse()
    {
        var fileStore = new Mock<ISMBFileStore>();
        SetupCreateFileFailure(fileStore);
        var service = CreateConnectedService(fileStore);

        Assert.IsFalse(service.ExistsFile(new Url("smb://user@host/Share/missing.txt")));
    }

    // ── Delete / Move ────────────────────────────────────────────────────────

    [TestMethod]
    public void DeleteFile_Success_ReturnsTrue()
    {
        var fileStore = new Mock<ISMBFileStore>();
        SetupCreateFileSuccess(fileStore);
        fileStore.Setup(fs => fs.SetFileInformation(It.IsAny<object>(), It.IsAny<FileInformation>())).Returns(NTStatus.STATUS_SUCCESS);
        var service = CreateConnectedService(fileStore);

        var result = service.DeleteFile(new Url("smb://user@host/Share/a.txt"), moveToRecycleBin: false);

        Assert.IsTrue(result);
        fileStore.Verify(fs => fs.SetFileInformation(It.IsAny<object>(), It.Is<FileInformation>(i => i is FileDispositionInformation)), Times.Once);
    }

    [TestMethod]
    public async Task MoveAsync_Success_SetsRenameInformation()
    {
        var fileStore = new Mock<ISMBFileStore>();
        SetupCreateFileSuccess(fileStore);
        fileStore.Setup(fs => fs.SetFileInformation(It.IsAny<object>(), It.IsAny<FileInformation>())).Returns(NTStatus.STATUS_SUCCESS);
        var service = CreateConnectedService(fileStore);

        await service.MoveAsync(new Url("smb://user@host/Share/a.txt"), new Url("smb://user@host/Share/b.txt"), true);

        fileStore.Verify(fs => fs.SetFileInformation(It.IsAny<object>(), It.Is<FileInformation>(i => i is FileRenameInformationType2)), Times.Once);
    }

    [TestMethod]
    public async Task MoveAsync_OpenFails_Throws()
    {
        var fileStore = new Mock<ISMBFileStore>();
        SetupCreateFileFailure(fileStore);
        var service = CreateConnectedService(fileStore);

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            service.MoveAsync(new Url("smb://user@host/Share/missing.txt"), new Url("smb://user@host/Share/b.txt"), true));
    }

    // ── Read / Write ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ReadFileAsync_ReadsUntilEndOfFile_ReturnsAllBytes()
    {
        var fileStore = new Mock<ISMBFileStore>();
        SetupCreateFileSuccess(fileStore);
        var firstChunk = new byte[] { 1, 2, 3 };
        byte[] emptyChunk = [];
        var callCount = 0;
        fileStore.Setup(fs => fs.ReadFile(out It.Ref<byte[]>.IsAny, It.IsAny<object>(), It.IsAny<long>(), It.IsAny<int>()))
            .Returns(new ReadFileDelegate((out byte[] data, object handle, long offset, int maxCount) =>
            {
                callCount++;
                data = callCount == 1 ? firstChunk : emptyChunk;
                return callCount == 1 ? NTStatus.STATUS_SUCCESS : NTStatus.STATUS_END_OF_FILE;
            }));

        var service = CreateConnectedService(fileStore);
        var file = await service.ReadFileAsync(new Url("smb://user@host/Share/a.txt"));

        Assert.IsNotNull(file);
        CollectionAssert.AreEqual(firstChunk, file!.Content);
    }

    private delegate NTStatus ReadFileDelegate(out byte[] data, object handle, long offset, int maxCount);

    [TestMethod]
    public async Task WriteFileAsync_WritesAllBytes()
    {
        var fileStore = new Mock<ISMBFileStore>();
        SetupCreateFileSuccess(fileStore);
        var written = 3;
        fileStore.Setup(fs => fs.WriteFile(out written, It.IsAny<object>(), It.IsAny<long>(), It.IsAny<byte[]>()))
            .Returns(NTStatus.STATUS_SUCCESS);
        var service = CreateConnectedService(fileStore);

        using var stream = new MemoryStream([1, 2, 3]);
        await service.WriteFileAsync(new Url("smb://user@host/Share/a.txt"), stream);

        fileStore.Verify(fs => fs.WriteFile(out written, It.IsAny<object>(), 0, It.Is<byte[]>(b => b.Length == 3)), Times.Once);
    }

    // ── GetAvailableDiskSpace ────────────────────────────────────────────────

    [TestMethod]
    public void GetAvailableDiskSpace_Success_ReturnsNonEmptyString()
    {
        var fileStore = new Mock<ISMBFileStore>();
        FileSystemInformation info = new FileFsFullSizeInformation
        {
            CallerAvailableAllocationUnits = 1000,
            SectorsPerAllocationUnit = 8,
            BytesPerSector = 512,
        };
        fileStore.Setup(fs => fs.GetFileSystemInformation(out info, FileSystemInformationClass.FileFsFullSizeInformation))
            .Returns(NTStatus.STATUS_SUCCESS);
        var service = CreateConnectedService(fileStore);

        var result = service.GetAvailableDiskSpace();

        Assert.IsFalse(string.IsNullOrEmpty(result));
    }

    [TestMethod]
    public void GetAvailableDiskSpace_NotConnected_ReturnsEmptyString()
    {
        var service = new SmbFileSystemService(() => new Mock<ISMBClient>().Object);

        Assert.AreEqual(string.Empty, service.GetAvailableDiskSpace());
    }
}

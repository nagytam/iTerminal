using iConsole.Data;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Nfs;

namespace iTerminal.Core.Test.Services.Nfs;

/// <summary>A progress sink that records synchronously, so a test reads the last report without waiting on a context.</summary>
internal sealed class RecordingProgress : IProgress<long>
{
    public long Last { get; private set; } = -1;

    public void Report(long value) => Last = value;
}

/// <summary>
/// The nfs:// drive's file system against a fake NFS export: the host lists its exports, an export lists its folders
/// first, file operations reach the right relative path, and a host or an export refuses what would change it.
/// </summary>
[TestClass]
public class NfsDriveServiceTest
{
    private const string Data = "nfs://nas/%2Fsrv%2Fdata/";

    private static FakeNfsMountFactory Factory()
    {
        var factory = new FakeNfsMountFactory();
        factory.Exports.AddRange(["/srv/data", "/home"]);
        var mount = new FakeNfsMount();
        mount.Listings["."] =
        [
            new NfsRemoteEntry("b.txt", false, 10, new DateTime(2026, 1, 2, 3, 4, 0)),
            new NfsRemoteEntry("docs", true, 0, null),
            new NfsRemoteEntry("A.txt", false, 2048, null),
        ];
        mount.Kinds["docs"] = NfsRemoteKind.Folder;
        mount.Listings["docs"] = [new NfsRemoteEntry("x.txt", false, 1, null)];
        mount.Kinds["docs/x.txt"] = NfsRemoteKind.File;
        mount.Files["docs/x.txt"] = [1];
        mount.Kinds["b.txt"] = NfsRemoteKind.File;
        mount.Files["b.txt"] = [1, 2, 3];
        factory.Mounts["nas|/srv/data"] = mount;
        return factory;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheHostListsItsExportsAsDriveObjectRowsInNameOrder()
    {
        var service = new NfsDriveService(Factory());

        var rows = await service.ListAsync(new Url("nfs://nas/"), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "/home", "/srv/data" }, rows.Select(r => r.Url.DisplayName).ToArray());
        Assert.IsTrue(rows.All(r => r is IDriveObjectEntry), "an export is a mount, not a file");
        Assert.AreEqual("nfs://nas/%2Fsrv%2Fdata/", rows[1].Url.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnExportListsItsFoldersFirstThenItsFilesByName()
    {
        var factory = Factory();
        var service = new NfsDriveService(factory);

        var rows = await service.ListAsync(new Url(Data), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "docs", "A.txt", "b.txt" }, rows.Select(r => r.Url.DisplayName).ToArray());
        Assert.AreEqual(PathType.Folder, rows[0].Url.PathType);
        Assert.AreEqual("nfs://nas/%2Fsrv%2Fdata/docs/", rows[0].Url.FullUrl);
        Assert.AreEqual("nfs://nas/%2Fsrv%2Fdata/b.txt", rows[2].Url.FullUrl);
        Assert.AreEqual(10, ((VirtualDriveEntry)rows[2]).Size, "the size is the server's, not the display text");
        Assert.IsFalse(rows.Any(r => r.Url.DisplayName == ".."), "the .. row is the panel's, not the service's");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFolderInsideAnExportListsByItsRelativePath()
    {
        var factory = Factory();
        var service = new NfsDriveService(factory);

        var rows = await service.ListAsync(new Url("nfs://nas/%2Fsrv%2Fdata/docs/"), CancellationToken.None);

        Assert.AreEqual("nfs://nas/%2Fsrv%2Fdata/docs/x.txt", rows.Single().Url.FullUrl);
        CollectionAssert.Contains(factory.On("nas", "/srv/data").Calls, "list docs");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ExistenceIsAnsweredByTheServerInsideAnExport()
    {
        var service = new NfsDriveService(Factory());

        Assert.IsTrue(service.ExistsFile(new Url("nfs://nas/%2Fsrv%2Fdata/b.txt")));
        Assert.IsFalse(service.ExistsFile(new Url("nfs://nas/%2Fsrv%2Fdata/missing.txt")));
        Assert.IsTrue(service.ExistsFolder(new Url("nfs://nas/%2Fsrv%2Fdata/docs/")));
        Assert.IsTrue(service.ExistsFolder(new Url("nfs://nas/")), "a host is a folder");
        Assert.IsFalse(service.ExistsFile(new Url("nfs://nas/")), "a host is not a file");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WhatIsWrittenIsReadBackFromTheSameRelativePath()
    {
        var factory = Factory();
        var service = new NfsDriveService(factory);
        var url = new Url("nfs://nas/%2Fsrv%2Fdata/docs/new.txt");

        using var content = new MemoryStream([9, 8, 7]);
        await service.WriteFileAsync(url, content);
        var file = await service.ReadFileAsync(url);

        CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, file!.Content);
        CollectionAssert.Contains(factory.On("nas", "/srv/data").Calls, "write docs/new.txt");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TransferProgressReportsTheBytesThatCrossed()
    {
        var service = new NfsDriveService(Factory());
        var progress = new RecordingProgress();

        using var destination = new MemoryStream();
        await service.StreamToAsync(new Url("nfs://nas/%2Fsrv%2Fdata/b.txt"), destination, progress, CancellationToken.None);

        Assert.AreEqual(3, destination.Length);
        Assert.AreEqual(3, progress.Last, "the last report is the total that crossed");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AHostAndAnExportRefuseDeleteAndCreate()
    {
        var service = new NfsDriveService(Factory());

        Assert.ThrowsExactly<IOException>(() => service.DeleteFile(new Url("nfs://nas/"), false));
        Assert.ThrowsExactly<IOException>(() => service.DeleteFolder(new Url(Data), true, false), "an export is a mount");
        Assert.ThrowsExactly<IOException>(() => service.CreateFolderAsync(new Url("nfs://nas/")).GetAwaiter().GetResult());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DeleteInsideAnExportReachesTheServerWithTheRelativePath()
    {
        var factory = Factory();
        var service = new NfsDriveService(factory);

        service.DeleteFolder(new Url("nfs://nas/%2Fsrv%2Fdata/docs/"), recursive: true, moveToRecycleBin: true);

        CollectionAssert.Contains(factory.On("nas", "/srv/data").Calls, "rmdir docs recursive=True");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MoveWithinAnExportIsARenameOnTheServer()
    {
        var factory = Factory();
        var service = new NfsDriveService(factory);

        await service.MoveAsync(new Url("nfs://nas/%2Fsrv%2Fdata/b.txt"), new Url("nfs://nas/%2Fsrv%2Fdata/c.txt"), overwrite: true);

        CollectionAssert.Contains(factory.On("nas", "/srv/data").Calls, "mv b.txt c.txt");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFolderCannotBeMovedBetweenExports()
    {
        var service = new NfsDriveService(Factory());

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            service.MoveAsync(new Url("nfs://nas/%2Fsrv%2Fdata/docs/"), new Url("nfs://nas/%2Fhome%2Fme/docs/"), overwrite: true));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileIsCopiedBetweenExportsAndTheSourceIsDeletedOnAMove()
    {
        var factory = Factory();
        var service = new NfsDriveService(factory);

        await service.MoveAsync(new Url("nfs://nas/%2Fsrv%2Fdata/b.txt"), new Url("nfs://nas/%2Fhome%2Fme/b.txt"), overwrite: true);

        var target = factory.Mounts["nas|/home/me"];
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, target.Files["b.txt"]);
        Assert.IsFalse(factory.On("nas", "/srv/data").Files.ContainsKey("b.txt"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AServerErrorReachesThePanelAsAnIOExceptionNamingTheHost()
    {
        var factory = Factory();
        factory.Mounts["nas|/srv/data"].Fail = new InvalidOperationException("stale handle");
        var service = new NfsDriveService(factory);

        var exception = Assert.ThrowsExactly<IOException>(() => service.ListAsync(new Url(Data), CancellationToken.None).GetAwaiter().GetResult());

        StringAssert.Contains(exception.Message, "nas");
        StringAssert.Contains(exception.Message, "stale handle");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFailedExistenceCheckThrowsRatherThanAnsweringNoFile()
    {
        var factory = Factory();
        factory.Mounts["nas|/srv/data"].Fail = new IOException("connection reset");
        var service = new NfsDriveService(factory);

        Assert.ThrowsExactly<IOException>(() => service.ExistsFile(new Url("nfs://nas/%2Fsrv%2Fdata/b.txt")),
            "a network error is not 'the file is not there'");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACopyThatCannotCheckItsTargetWritesNothing()
    {
        var factory = Factory();
        var mount = factory.Mounts["nas|/srv/data"];
        mount.Fail = new IOException("connection reset");
        var service = new NfsDriveService(factory);

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            service.CopyAsync(new Url("nfs://nas/%2Fsrv%2Fdata/b.txt"), new Url("nfs://nas/%2Fsrv%2Fdata/A.txt"), overwrite: false));

        Assert.IsFalse(mount.Calls.Any(call => call.StartsWith("write", StringComparison.Ordinal)), "the existing target is not replaced");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CreatingAFolderThatIsAlreadyThereDoesNotAskTheServerToMakeIt()
    {
        var factory = Factory();
        var service = new NfsDriveService(factory);

        await service.CreateFolderAsync(new Url("nfs://nas/%2Fsrv%2Fdata/docs/"));

        CollectionAssert.DoesNotContain(factory.On("nas", "/srv/data").Calls, "mkdir docs");
    }
}

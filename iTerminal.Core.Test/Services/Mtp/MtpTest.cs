using System.Text;
using iConsole.Data;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Mtp;
using iTerminal.Core.Test.Helper;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Services.Mtp;

/// <summary>
/// The MTP drive against a fake backend and recorded tool output. Nothing here talks to a phone,
/// <c>gio</c> or the Windows shell.
/// </summary>
[TestClass]
public class MtpTest
{
    private sealed class FakeBackend : IMtpBackend
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

        public List<string> Deleted { get; } = [];

        public string Unavailable { get; set; } = string.Empty;

        public Task<IReadOnlyList<string>> GetDevicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["Pixel 7", "Camera"]);

        public Task<IReadOnlyList<string>> GetStoragesAsync(string device, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["SD card", "Internal storage"]);

        public Task<IReadOnlyList<MtpItem>> ListAsync(string device, string path, CancellationToken cancellationToken)
        {
            var prefix = path + "/";
            var items = new Dictionary<string, MtpItem>();
            foreach (var key in Files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
            {
                var rest = key[prefix.Length..];
                var slash = rest.IndexOf('/');
                var name = slash < 0 ? rest : rest[..slash];
                items[name] = slash < 0
                    ? new MtpItem(name, false, Files[key].Length, new DateTime(2026, 10, 5, 9, 30, 0))
                    : new MtpItem(name, true, 0, null);
            }

            return Task.FromResult<IReadOnlyList<MtpItem>>([.. items.Values]);
        }

        public Task DownloadAsync(string device, string path, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            destination.Write(Files[path]);
            return Task.CompletedTask;
        }

        public Task UploadAsync(string device, string path, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            Files[path] = buffer.ToArray();
            return Task.CompletedTask;
        }

        public Task CreateFolderAsync(string device, string path, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(string device, string path, bool isFolder, CancellationToken cancellationToken)
        {
            Deleted.Add(path);
            Files.Remove(path);
            return Task.CompletedTask;
        }
    }

    private static (MtpDriveService Service, FakeBackend Backend) Create()
    {
        var backend = new FakeBackend();
        backend.Files["Internal storage/DCIM/cat.jpg"] = Encoding.UTF8.GetBytes("meow");
        backend.Files["Internal storage/DCIM/Camera/dog.jpg"] = Encoding.UTF8.GetBytes("woof");
        backend.Files["Internal storage/notes.txt"] = Encoding.UTF8.GetBytes("hi");
        return (new MtpDriveService(backend), backend);
    }

    #region URLs

    [Timeout(60_000)]
    [TestMethod]
    public void MtpUrl_IsAVirtualDriveWithTheDeviceAsHost()
    {
        var url = new Url("mtp://Pixel 7/Internal storage/DCIM/Camera/");

        Assert.AreEqual(ContainerType.Mtp, url.ContainerType);
        Assert.AreEqual("Pixel 7", url.Host);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual("Camera", url.Name);
        Assert.AreEqual("mtp://Pixel 7/Internal storage/DCIM/", url.ParentUrl.FullUrl);
        Assert.AreEqual("mtp://Pixel 7/Internal storage/", url.RootUrl.FullUrl);
        Assert.AreEqual(("Pixel 7", "Internal storage/DCIM/Camera"), MtpDriveService.Split(url));
        Assert.AreEqual(("", ""), MtpDriveService.Split(new Url(Container.MTP)));
    }

    #endregion

    #region The service

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheRootListsDevices_ADeviceItsStorages_AndBothAreObjectsNotFiles()
    {
        var (service, _) = Create();

        var devices = await service.ListAsync(new Url(Container.MTP), CancellationToken.None);
        var storages = await service.ListAsync(new Url("mtp://Pixel 7/"), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "Camera", "Pixel 7" }, devices.Select(d => d.Url.DisplayName).ToArray());
        CollectionAssert.AreEqual(new[] { "Internal storage", "SD card" }, storages.Select(s => s.Url.DisplayName).ToArray());
        Assert.IsTrue(devices.All(d => d is IDriveObjectEntry));
        Assert.AreEqual("mtp://Pixel 7/Internal storage/", storages[0].Url.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFolder_ListsFoldersFirst_WithSizesAndDates()
    {
        var (service, _) = Create();

        var entries = await service.ListAsync(new Url("mtp://Pixel 7/Internal storage/DCIM/"), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "Camera", "cat.jpg" }, entries.Select(e => e.Url.DisplayName).ToArray());
        Assert.AreEqual("mtp://Pixel 7/Internal storage/DCIM/Camera/", entries[0].Url.FullUrl);
        Assert.AreEqual(PathType.Folder, entries[0].Url.PathType);
        Assert.AreEqual("mtp://Pixel 7/Internal storage/DCIM/cat.jpg", entries[1].Url.FullUrl);
        Assert.AreEqual("2026-10-05", entries[1].Date);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadWriteAndCopy_GoThroughTheBackend()
    {
        var (service, backend) = Create();
        var cat = new Url("mtp://Pixel 7/Internal storage/DCIM/cat.jpg");
        var copy = new Url("mtp://Pixel 7/Internal storage/Pictures/cat.jpg");

        Assert.AreEqual("meow", Encoding.UTF8.GetString((await service.ReadFileAsync(cat))!.Content!));
        await service.CopyAsync(cat, copy, overwrite: false);
        await service.WriteFileAsync(new Url("mtp://Pixel 7/Internal storage/new.txt"), new MemoryStream(Encoding.UTF8.GetBytes("new")));

        Assert.AreEqual("meow", Encoding.UTF8.GetString(backend.Files["Internal storage/Pictures/cat.jpg"]));
        Assert.AreEqual("new", Encoding.UTF8.GetString(backend.Files["Internal storage/new.txt"]));
        Assert.IsTrue(service.ExistsFile(copy));
        Assert.IsFalse(service.ExistsFile(new Url("mtp://Pixel 7/Internal storage/nope.txt")));
        Assert.IsTrue(service.ExistsFolder(new Url("mtp://Pixel 7/Internal storage/DCIM/")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMoveOfAFile_IsACopyThenADelete_AndAFolderIsRefused()
    {
        var (service, backend) = Create();

        await service.MoveAsync(
            new Url("mtp://Pixel 7/Internal storage/notes.txt"),
            new Url("mtp://Pixel 7/Internal storage/moved.txt"),
            overwrite: false);

        Assert.IsFalse(backend.Files.ContainsKey("Internal storage/notes.txt"));
        Assert.IsTrue(backend.Files.ContainsKey("Internal storage/moved.txt"));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            service.MoveAsync(new Url("mtp://Pixel 7/Internal storage/DCIM/"), new Url("mtp://Pixel 7/Internal storage/Other/"), true));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ADeviceOrStorage_IsNeverDeletedOrWrittenTo()
    {
        var (service, backend) = Create();

        foreach (var url in new[] { "mtp://Pixel 7/", "mtp://Pixel 7/Internal storage/" })
        {
            var failed = false;
            try
            {
                service.DeleteFolder(new Url(url), recursive: true, moveToRecycleBin: false);
            }
            catch (IOException)
            {
                failed = true;
            }

            Assert.IsTrue(failed, $"{url} must be refused");
        }

        Assert.AreEqual(0, backend.Deleted.Count);
        service.DeleteFile(new Url("mtp://Pixel 7/Internal storage/notes.txt"), moveToRecycleBin: true);
        CollectionAssert.AreEqual(new[] { "Internal storage/notes.txt" }, backend.Deleted);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutAStack_ListingSaysWhy()
    {
        var service = new MtpDriveService(new UnavailableMtpBackend("No MTP here."));

        var exception = await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.ListAsync(new Url(Container.MTP), CancellationToken.None));

        Assert.AreEqual("No MTP here.", exception.Message);
    }

    #endregion

    #region gio

    [Timeout(60_000)]
    [TestMethod]
    public void Gio_FindsDevicesInTheMountList()
    {
        const string Output = """
            Drive(0): SATA SSD
              Type: GProxyDrive (GProxyVolumeMonitorUDisks2)
            Mount(0): Pixel 7 -> mtp://Pixel_7_1A2B3C/
              Type: GDaemonMount
              default_location=mtp://Pixel_7_1A2B3C/
              themed icons:  [phone]
            Mount(1): Pixel 7 -> mtp://Pixel_7_1A2B3C/
            """;

        CollectionAssert.AreEqual(new[] { "Pixel_7_1A2B3C" }, GioMtpBackend.ParseDevices(Output).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Gio_ReadsAListing()
    {
        var output = "DCIM\t0\t(directory)\ttime::modified=1790000000\n"
            + "my photo.jpg\t2048\t(regular)\ttime::modified=1790000100\n"
            + "weird line\n";

        var items = GioMtpBackend.ParseListing(output);

        Assert.AreEqual(2, items.Count);
        Assert.IsTrue(items[0].IsFolder);
        Assert.AreEqual("my photo.jpg", items[1].Name);
        Assert.AreEqual(2048, items[1].Size);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1790000100).LocalDateTime, items[1].Modified);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Gio_EscapesEachSegmentIntoTheUri_AndNeverUsesAShell()
    {
        var runner = new FakeProcessRunner()
            .Answer("gio list -l", "a b.txt\t3\t(regular)\n")
            .AnswerBytes("gio cat", Encoding.UTF8.GetBytes("abc"))
            .Answer("gio remove", string.Empty);
        var backend = new GioMtpBackend(runner);

        var items = await backend.ListAsync("Pixel_7_1", "Internal storage/My #Photos", CancellationToken.None);
        using var buffer = new MemoryStream();
        await backend.DownloadAsync("Pixel_7_1", "Internal storage/a b.txt", buffer, null, CancellationToken.None);
        await backend.DeleteAsync("Pixel_7_1", "Internal storage/a b.txt", isFolder: false, CancellationToken.None);

        Assert.AreEqual("a b.txt", items[0].Name);
        Assert.AreEqual("abc", Encoding.UTF8.GetString(buffer.ToArray()));
        var calls = runner.Calls.Select(c => c.CommandLine).ToList();
        StringAssert.Contains(calls[0], "mtp://Pixel_7_1/Internal%20storage/My%20%23Photos/");
        StringAssert.Contains(calls[1], "mtp://Pixel_7_1/Internal%20storage/a%20b.txt");
        Assert.IsTrue(calls.All(c => c.StartsWith("gio ", StringComparison.Ordinal)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Gio_UploadsThroughSaveWithTheBytesOnStdin()
    {
        var runner = new FakeProcessRunner().Answer("gio save", string.Empty);
        var backend = new GioMtpBackend(runner);

        await backend.UploadAsync("Pixel_7_1", "Internal storage/up.txt", new MemoryStream(Encoding.UTF8.GetBytes("payload")), null, CancellationToken.None);

        Assert.AreEqual("payload", Encoding.UTF8.GetString(runner.Calls[0].StandardInput));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Gio_ExplainsTheCommonFailures()
    {
        var notMounted = new iTerminal.Core.Services.Processes.ProcessOutput(2, string.Empty, "gio: mtp://x/: Location is not mounted");

        StringAssert.Contains(GioMtpBackend.Describe(notMounted), "not mounted");
    }

    #endregion

    #region The Windows shell

    [Timeout(60_000)]
    [TestMethod]
    public void Shell_QuotesValuesAsPowerShellLiterals()
    {
        Assert.AreEqual("'Tom''s phone'", ShellMtpBackend.Quote("Tom's phone"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Shell_ReadsRowsAndNames_ForOneItemAsForMany()
    {
        var items = ShellMtpBackend.ParseRows("""[{"name":"DCIM","folder":true,"size":0,"modified":"2026-10-05T09:30:00.0000000Z"},{"name":"a.txt","folder":false,"size":12,"modified":"1601-01-01T00:00:00.0000000Z"}]""");

        Assert.AreEqual(2, items.Count);
        Assert.IsTrue(items[0].IsFolder);
        Assert.IsNotNull(items[0].Modified);
        Assert.IsNull(items[1].Modified, "an unknown date is not 1601");
        Assert.AreEqual(12, items[1].Size);

        CollectionAssert.AreEqual(new[] { "Pixel 7" }, ShellMtpBackend.ParseNames("\"Pixel 7\"").ToArray());
        CollectionAssert.AreEqual(new[] { "A", "B" }, ShellMtpBackend.ParseNames("[\"A\",\"B\"]").ToArray());
        Assert.AreEqual(0, ShellMtpBackend.ParseNames("[]").Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Shell_PassesNamesAsLiterals_AndRefusesToDelete()
    {
        var runner = new FakeProcessRunner().Answer("powershell", "[]");
        var backend = new ShellMtpBackend(runner);

        await backend.ListAsync("Tom's phone", "Internal storage/DCIM", CancellationToken.None);

        var script = runner.Calls[0].Arguments[^1];
        StringAssert.Contains(script, "Get-PathFolder 'Tom''s phone' @('Internal storage','DCIM')");
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            backend.DeleteAsync("Pixel", "Internal storage/a.txt", false, CancellationToken.None));
    }

    #endregion
}

using iConsole.Data;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Bluetooth;
using iTerminal.Core.Services.Obex;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.Obex.Test;

/// <summary>The obex:// drive over a fake backend: the paired devices at the root, a device's folders below.</summary>
[TestClass]
public class ObexDriveServiceTest
{
    /// <summary>A device list with a fixed answer, the way the Bluetooth dialog sees it.</summary>
    private sealed class FixedBluetooth(params BluetoothDevice[] devices) : IBluetoothService
    {
        public bool IsSupported => true;

        public Task<BluetoothListing> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BluetoothListing(devices, null));

        public Task<BluetoothOutcome> ScanAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BluetoothOutcome> ConnectAsync(string address, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BluetoothOutcome> DisconnectAsync(string address, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BluetoothOutcome> PairAsync(string address, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BluetoothOutcome> RemoveAsync(string address, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>A phone's folders, and the bytes its files hold, in memory.</summary>
    private sealed class MemoryPhone : IObexBackend
    {
        public string Unavailable => string.Empty;

        public Dictionary<string, List<ObexItem>> Folders { get; } = [];

        public Dictionary<string, byte[]> Files { get; } = [];

        public Task<IReadOnlyList<ObexItem>> ListAsync(string address, string path, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObexItem>>(Folders.GetValueOrDefault(path) ?? []);

        public Task DownloadAsync(string address, string path, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            destination.Write(Files[path]);
            return Task.CompletedTask;
        }

        public Task UploadAsync(string address, string path, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            Files[path] = buffer.ToArray();
            return Task.CompletedTask;
        }

        public Task CreateFolderAsync(string address, string path, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(string address, string path, bool isFolder, CancellationToken cancellationToken)
        {
            Files.Remove(path);
            return Task.CompletedTask;
        }
    }

    private const string Phone = "AA:BB:CC:DD:EE:FF";

    private static BluetoothDevice Paired(string name, string address) => new(address, name, IsPaired: true, IsConnected: false);

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheRoot_ListsThePairedDevices_AndNoUnpairedOnes()
    {
        var service = new ObexDriveService(new MemoryPhone(), new FixedBluetooth(
            Paired("Pixel", Phone),
            new BluetoothDevice("11:22:33:44:55:66", "Keyboard", IsPaired: false, IsConnected: false)));

        var rows = await service.ListAsync(new Url("obex://"), CancellationToken.None);

        var device = rows.Single();
        Assert.AreEqual("obex://" + Phone + "/", device.Url.FullUrl);
        Assert.IsInstanceOfType<IDriveObjectEntry>(device);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADeviceFolder_ListsItsFoldersFirst_WithTheRightUrls()
    {
        var phone = new MemoryPhone();
        phone.Folders[""] = [new ObexItem("a.jpg", false, 2048, null), new ObexItem("DCIM", true, 0, null)];
        var service = new ObexDriveService(phone, new FixedBluetooth());

        var rows = await service.ListAsync(new Url("obex://" + Phone + "/"), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "DCIM", "a.jpg" }, rows.Select(r => r.Url.Name).ToArray());
        Assert.AreEqual("obex://" + Phone + "/DCIM/", rows[0].Url.FullUrl);
        Assert.AreEqual(PathType.File, rows[1].Url.PathType);
        Assert.AreEqual("obex://" + Phone + "/a.jpg", rows[1].Url.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFile_IsReadAndWrittenThroughTheBackend()
    {
        var phone = new MemoryPhone { Files = { ["DCIM/a.jpg"] = [9, 8, 7] } };
        var service = new ObexDriveService(phone, new FixedBluetooth());
        var url = new Url("obex://" + Phone + "/DCIM/a.jpg");

        var read = await service.ReadFileAsync(url);
        await service.WriteFileAsync(new Url("obex://" + Phone + "/DCIM/b.jpg"), new MemoryStream([1, 2]));

        CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, read!.Content);
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, phone.Files["DCIM/b.jpg"]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFolder_CannotBeMoved_AndADevice_CannotBeDeleted()
    {
        var service = new ObexDriveService(new MemoryPhone(), new FixedBluetooth());

        await AsyncAssert.ThrowsAsync<NotSupportedException>(() => service.MoveAsync(
            new Url("obex://" + Phone + "/DCIM/"), new Url("obex://" + Phone + "/Backup/"), overwrite: true));
        var error = await AsyncAssert.ThrowsAsync<IOException>(() =>
        {
            service.DeleteFile(new Url("obex://" + Phone + "/"), moveToRecycleBin: false);
            return Task.CompletedTask;
        });
        StringAssert.Contains(error.Message, "not a file");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WhereTheSystemHasNoObex_TheRootSaysWhy()
    {
        var service = new ObexDriveService(new UnavailableObexBackend("Bluetooth file transfer can only be browsed on Linux (BlueZ)."), new FixedBluetooth());

        var error = await AsyncAssert.ThrowsAsync<NotSupportedException>(() => service.ListAsync(new Url("obex://"), CancellationToken.None));

        Assert.AreEqual("Bluetooth file transfer can only be browsed on Linux (BlueZ).", error.Message);
    }
}

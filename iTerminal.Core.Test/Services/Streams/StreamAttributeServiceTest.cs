using System.Text;
using iTerminal.Core.Services.Streams;

namespace iTerminal.Core.Test.Services.Streams;

[TestClass]
public class StreamAttributeServiceTest
{
    private readonly StreamAttributeService _service = new();

    [TestMethod]
    [Timeout(15_000)]
    public void ParseWindowsStreamName_KeepsDataStreamsAndDropsTheMainOne()
    {
        Assert.AreEqual("Zone.Identifier", StreamAttributeService.ParseWindowsStreamName(":Zone.Identifier:$DATA"));
        Assert.IsNull(StreamAttributeService.ParseWindowsStreamName("::$DATA"), "the file itself");
        Assert.IsNull(StreamAttributeService.ParseWindowsStreamName(":$I30:$INDEX_ALLOCATION"), "not a data stream");
        Assert.IsNull(StreamAttributeService.ParseWindowsStreamName("plain"));
    }

    [TestMethod]
    [Timeout(15_000)]
    public void ParseAttributeNames_SplitsTheNulSeparatedList()
    {
        byte[] buffer = Encoding.UTF8.GetBytes("user.comment\0user.é\0security.selinux\0");

        CollectionAssert.AreEqual(new[] { "user.comment", "user.é", "security.selinux" }, StreamAttributeService.ParseAttributeNames(buffer).ToArray());
        Assert.AreEqual(0, StreamAttributeService.ParseAttributeNames([]).Count);
    }

    [TestMethod]
    [Timeout(15_000)]
    public void IsValidName_RefusesWhatCouldAddressAnotherFile()
    {
        Assert.IsFalse(StreamAttributeService.IsValidName(null));
        Assert.IsFalse(StreamAttributeService.IsValidName(""));
        Assert.IsFalse(StreamAttributeService.IsValidName("a\0b"));
        Assert.IsTrue(StreamAttributeService.IsValidName("Zone.Identifier"));
        if (OperatingSystem.IsWindows())
        {
            foreach (string bad in new[] { "x:y", @"..\other.txt", "a/b", "*", ".." })
            {
                Assert.IsFalse(StreamAttributeService.IsValidName(bad), bad);
            }
        }
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task ABadName_NeverReachesTheSystem_AndOtherFilesAreUntouched()
    {
        using TempFolder folder = new();
        string file = folder.File("a.txt", "keep");
        string other = folder.File("other.txt", "keep too");
        string bad = OperatingSystem.IsWindows() ? @"..\other.txt" : "bad\0name";

        StreamResult result = await _service.DeleteAsync(file, bad);
        StreamData read = await _service.ReadAsync(file, bad, 100);

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(read.Failure);
        Assert.AreEqual("keep too", File.ReadAllText(other));
        Assert.AreEqual("keep", File.ReadAllText(file));
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task OnWindows_ListsReadsAndDeletesAStream_AndLeavesTheFileAlone()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("NTFS streams are Windows only.");
        }

        using TempFolder folder = new();
        string file = folder.File("a.txt", "main contents");
        if (!folder.TryWriteStream(file, "note", "hello stream"))
        {
            Assert.Inconclusive("The temporary volume does not keep alternate data streams.");
        }

        folder.TryWriteStream(file, "empty", string.Empty);

        StreamListing listing = await _service.ListAsync(file);
        Assert.IsNull(listing.Failure);
        CollectionAssert.AreEquivalent(new[] { "note", "empty" }, listing.Items.Select(i => i.Name).ToArray());
        Assert.AreEqual(12, listing.Items.Single(i => i.Name == "note").Size);

        StreamData content = await _service.ReadAsync(file, "note", 5);
        Assert.AreEqual("hello", Encoding.UTF8.GetString(content.Bytes));
        Assert.AreEqual(12, content.TotalSize);
        Assert.IsTrue(content.IsTruncated);

        StreamResult deleted = await _service.DeleteAsync(file, "note");
        Assert.IsTrue(deleted.Success, deleted.Message);
        CollectionAssert.AreEqual(new[] { "empty" }, (await _service.ListAsync(file)).Items.Select(i => i.Name).ToArray());
        Assert.AreEqual("main contents", File.ReadAllText(file));
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task OnWindows_AFolderCarriesStreamsToo()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("NTFS streams are Windows only.");
        }

        using TempFolder folder = new();
        string sub = Path.Combine(folder.Path, "sub");
        Directory.CreateDirectory(sub);
        Assert.AreEqual(0, (await _service.ListAsync(sub)).Items.Count, "a folder with none");
        if (!folder.TryWriteStream(sub, "tag", "x"))
        {
            Assert.Inconclusive("The temporary folder cannot take a stream on a folder.");
        }

        Assert.AreEqual("tag", (await _service.ListAsync(sub)).Items.Single().Name);
        Assert.AreEqual("x", Encoding.UTF8.GetString((await _service.ReadAsync(sub, "tag", 10)).Bytes));
        Assert.IsTrue((await _service.DeleteAsync(sub, "tag")).Success);
        Assert.AreEqual(0, (await _service.ListAsync(sub)).Items.Count);
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task OnWindows_AMissingFileIsASentence_NotAnException()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows only.");
        }

        StreamListing listing = await _service.ListAsync(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N")));

        Assert.IsFalse(string.IsNullOrEmpty(listing.Failure));
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task OnLinux_ListsReadsAndRemovesAnExtendedAttribute()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Needs setfattr on Linux.");
        }

        using TempFolder folder = new();
        string file = folder.File("a.txt", "main");
        if (!folder.TrySetAttribute(file, "user.note", "hello"))
        {
            Assert.Inconclusive("setfattr is not installed or the volume refuses extended attributes.");
        }

        StreamListing listing = await _service.ListAsync(file);
        Assert.AreEqual("user.note", listing.Items.Single().Name);
        Assert.AreEqual("hello", Encoding.UTF8.GetString((await _service.ReadAsync(file, "user.note", 100)).Bytes));
        Assert.IsTrue((await _service.DeleteAsync(file, "user.note")).Success);
        Assert.AreEqual(0, (await _service.ListAsync(file)).Items.Count);
        Assert.AreEqual("main", File.ReadAllText(file));
    }

    private sealed class TempFolder : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("iTerminal.Streams.");

        public string Path => _directory.FullName;

        public string File(string name, string text)
        {
            string path = System.IO.Path.Combine(Path, name);
            System.IO.File.WriteAllText(path, text);
            return path;
        }

        public bool TryWriteStream(string path, string name, string text)
        {
            try
            {
                System.IO.File.WriteAllText(path + ":" + name, text);
                return true;
            }
            catch (Exception exception) when (exception is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }
        }

        public bool TrySetAttribute(string path, string name, string value)
        {
            try
            {
                using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("setfattr", ["-n", name, "-v", value, path]) { RedirectStandardError = true });
                process?.WaitForExit();
                return process?.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        public void Dispose() => _directory.Delete(recursive: true);
    }
}

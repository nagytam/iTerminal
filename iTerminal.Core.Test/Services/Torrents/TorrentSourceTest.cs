using iTerminal.Core.Services;
using iTerminal.Core.Services.Torrents;

namespace iTerminal.Core.Test.Services.Torrents;

[TestClass]
public class TorrentSourceTest
{
    [TestMethod]
    [DataRow("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567", TorrentSourceKind.Magnet)]
    [DataRow("magnet:nonsense", TorrentSourceKind.Invalid)]
    [DataRow("https://example.com/a.torrent", TorrentSourceKind.Url)]
    [DataRow("  \"C:\\Downloads\\a.torrent\"  ", TorrentSourceKind.File)]
    [DataRow("/home/me/a.torrent", TorrentSourceKind.File)]
    [DataRow("", TorrentSourceKind.Invalid)]
    [DataRow(null, TorrentSourceKind.Invalid)]
    public void ItTellsTheKindsApart(string? text, TorrentSourceKind expected) =>
        Assert.AreEqual(expected, TorrentSource.Classify(text));

    [TestMethod]
    public void ItStripsTheQuotesAFileManagerAdds() =>
        Assert.AreEqual(@"C:\a b\c.torrent", TorrentSource.Clean(" \"C:\\a b\\c.torrent\" "));

    [TestMethod]
    public async Task AFileThatIsNotATorrentIsRefusedWithoutStartingAnything()
    {
        string folder = Path.Combine(Path.GetTempPath(), "torrent-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string bad = Path.Combine(folder, "bad.torrent");
            await File.WriteAllTextAsync(bad, "not bencoded");
            await using TorrentService service = new(new MemorySettings(), folder);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(bad, folder));

            Assert.AreEqual(0, service.Snapshot().Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task AMissingFileIsReported()
    {
        string folder = Path.Combine(Path.GetTempPath(), "torrent-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await using TorrentService service = new(new MemorySettings(), folder);

            await Assert.ThrowsAsync<FileNotFoundException>(() => service.AddAsync(Path.Combine(folder, "nope.torrent"), folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class MemorySettings : ISettingsService
    {
        private readonly Dictionary<string, string> _values = [];
        public bool IsDirty => false;
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public bool? GetAsBool(string key, bool? defaultValue = false) => bool.TryParse(Get(key), out bool v) ? v : defaultValue;
        public void Set(string key, string value) => _values[key] = value;
        public void Write() { }
    }
}

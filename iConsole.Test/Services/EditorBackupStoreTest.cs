using iConsole.Services;

namespace iConsole.Test.Services;

[TestClass]
public class EditorBackupStoreTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "iTerminal-backup-test-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static EditorBackup Sample(params string[] lines) =>
        new(new DateTime(2026, 11, 13, 9, 30, 0, DateTimeKind.Utc), lines, "\r\n", "utf-8");

    [Timeout(60_000)]
    [TestMethod]
    public void WriteThenRead_RoundTripsTheLinesAndTheMetadata()
    {
        EditorBackupStore store = new(_folder);
        store.Write("file://C:/notes/a.txt", Sample("first", "", "  second \t"));

        EditorBackup? read = store.Read("file://C:/notes/a.txt");

        Assert.IsNotNull(read);
        CollectionAssert.AreEqual(new[] { "first", "", "  second \t" }, read.Lines);
        Assert.AreEqual("\r\n", read.LineEnding);
        Assert.AreEqual("utf-8", read.EncodingName);
        Assert.AreEqual(DateTimeKind.Utc, read.SavedAtUtc.Kind);
        Assert.AreEqual(new DateTime(2026, 11, 13, 9, 30, 0, DateTimeKind.Utc), read.SavedAtUtc);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Write_ReplacesTheEarlierBackupOfTheSameFile()
    {
        EditorBackupStore store = new(_folder);
        store.Write("k", Sample("old"));
        store.Write("k", Sample("new"));

        CollectionAssert.AreEqual(new[] { "new" }, store.Read("k")!.Lines);
        Assert.IsFalse(File.Exists(store.PathFor("k") + ".tmp"), "the temporary file is moved, not left behind");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_OfAnotherFilesBackup_IsNull_EvenIfItsHashWereTheSame()
    {
        EditorBackupStore store = new(_folder);
        store.Write("file://one", Sample("x"));
        File.Copy(store.PathFor("file://one"), store.PathFor("file://two"));

        Assert.IsNull(store.Read("file://two"), "the backup records the file it belongs to");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Read_OfAMissingOrDamagedBackup_IsNull()
    {
        EditorBackupStore store = new(_folder);
        Assert.IsNull(store.Read("nothing"));

        Directory.CreateDirectory(_folder);
        File.WriteAllText(store.PathFor("broken"), "{ not json");
        Assert.IsNull(store.Read("broken"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Delete_RemovesTheBackup_AndIsQuietWhenThereIsNone()
    {
        EditorBackupStore store = new(_folder);
        store.Write("k", Sample("x"));

        store.Delete("k");
        Assert.IsNull(store.Read("k"));

        store.Delete("k");
        store.Delete("never-written");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathFor_IsStableAndDiffersByKey_AndStaysInTheFolder()
    {
        EditorBackupStore store = new(_folder);
        Assert.AreEqual(store.PathFor("a"), store.PathFor("a"));
        Assert.AreNotEqual(store.PathFor("a"), store.PathFor("b"));
        Assert.AreEqual(_folder, Path.GetDirectoryName(store.PathFor("a")));
    }
}

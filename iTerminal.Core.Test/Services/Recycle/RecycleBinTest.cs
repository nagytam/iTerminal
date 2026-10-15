using iConsole.Data;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Recycle;
using System.Text;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Services.Recycle;

/// <summary>
/// The recycle bin, on bins built in a temporary folder: no test here reads, restores from or
/// empties the bin of the machine it runs on.
/// </summary>
[TestClass]
public class RecycleBinTest
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 30, 0, DateTimeKind.Local);

    private string _root = null!;

    private string Bin => Path.Combine(_root, "bin");

    private string Home => Path.Combine(_root, "home");

    [TestInitialize]
    public void CreateSandbox()
    {
        _root = Directory.CreateTempSubdirectory("iTerminal.Recycle.").FullName;
        Directory.CreateDirectory(Bin);
        Directory.CreateDirectory(Home);
    }

    [TestCleanup]
    public void DeleteSandbox()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A deleted file (or, with null content, a deleted folder) as Windows keeps it.</summary>
    private void Recycle(string id, string originalPath, string? content, DateTime deleted)
    {
        var extension = Path.GetExtension(originalPath);
        var contentPath = Path.Combine(Bin, "$R" + id + extension);
        if (content is null)
        {
            Directory.CreateDirectory(contentPath);
            File.WriteAllText(Path.Combine(contentPath, "inside.txt"), "inside");
        }
        else
        {
            File.WriteAllText(contentPath, content);
        }

        File.WriteAllBytes(Path.Combine(Bin, "$I" + id + extension), WindowsRecycleBinStore.BuildRecord(originalPath, content?.Length ?? 0, deleted));
    }

    private WindowsRecycleBinStore Store() => new(() => [Bin, Path.Combine(_root, "no-such-volume")]);

    private RecycleBinService Service() => new(Store());

    #region The Windows record
    [TestMethod]
    public void ParseRecord_Version2_AsWindows10WritesIt()
    {
        var parsed = WindowsRecycleBinStore.ParseRecord(WindowsRecycleBinStore.BuildRecord(@"C:\Users\me\Árvíz report.pdf", 1234, Monday));

        Assert.AreEqual((@"C:\Users\me\Árvíz report.pdf", 1234L, Monday), parsed);
    }

    [TestMethod]
    public void ParseRecord_Version1_AFixedPathField()
    {
        // Vista to 8.1: no length, 260 UTF-16 characters, the path null-terminated inside them.
        var bytes = new byte[24 + 520];
        BitConverter.GetBytes(1L).CopyTo(bytes, 0);
        BitConverter.GetBytes(99L).CopyTo(bytes, 8);
        BitConverter.GetBytes(Monday.ToFileTime()).CopyTo(bytes, 16);
        Encoding.Unicode.GetBytes(@"D:\old\notes.txt").CopyTo(bytes, 24);

        Assert.AreEqual((@"D:\old\notes.txt", 99L, Monday), WindowsRecycleBinStore.ParseRecord(bytes));
    }

    [TestMethod]
    public void ParseRecord_WhatIsNotARecord_IsNull()
    {
        Assert.IsNull(WindowsRecycleBinStore.ParseRecord([]));
        Assert.IsNull(WindowsRecycleBinStore.ParseRecord(new byte[24]));
        Assert.IsNull(WindowsRecycleBinStore.ParseRecord(new byte[64]), "version 0");

        // A length that claims more characters than the file holds is read as far as the file goes.
        var truncated = WindowsRecycleBinStore.BuildRecord(@"C:\a\b.txt", 1, Monday)[..40];
        Assert.AreEqual(@"C:\a\b", WindowsRecycleBinStore.ParseRecord(truncated)?.OriginalPath);
    }
    #endregion

    #region The Windows store
    [TestMethod]
    public void List_PairsEveryRecordWithItsContent_AndSkipsWhatIsIncomplete()
    {
        Recycle("AAA111", Path.Combine(Home, "report.pdf"), "pdf bytes", Monday);
        Recycle("BBB222", Path.Combine(Home, "photos"), null, Monday.AddHours(1));
        File.WriteAllBytes(Path.Combine(Bin, "$ICCC333.txt"), WindowsRecycleBinStore.BuildRecord(Path.Combine(Home, "orphan.txt"), 1, Monday));
        File.WriteAllText(Path.Combine(Bin, "$RDDD444.txt"), "content without a record");
        File.WriteAllText(Path.Combine(Bin, "$IEEE555.txt"), "not a record");
        File.WriteAllText(Path.Combine(Bin, "$REEE555.txt"), "x");
        File.WriteAllText(Path.Combine(Bin, "desktop.ini"), "[.ShellClassInfo]");

        var items = Store().List().OrderBy(i => i.Name).ToList();

        CollectionAssert.AreEqual(new[] { "photos", "report.pdf" }, items.Select(i => i.Name).ToArray());
        Assert.IsTrue(items[0].IsFolder);
        Assert.IsFalse(items[1].IsFolder);
        Assert.AreEqual(Path.Combine(Home, "report.pdf"), items[1].OriginalPath);
        Assert.AreEqual(9, items[1].Size);
        Assert.AreEqual(Monday, items[1].Deleted);
    }

    [TestMethod]
    public void Restore_MovesTheFileBack_RecreatingItsFolder_AndRemovesTheRecord()
    {
        var original = Path.Combine(Home, "gone-folder", "report.pdf");
        Recycle("AAA111", original, "pdf bytes", Monday);
        var store = Store();

        store.Restore(store.List().Single());

        Assert.AreEqual("pdf bytes", File.ReadAllText(original));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(Bin).Length, "nothing of it is left in the bin");
    }

    [TestMethod]
    public void Restore_AFolder_ComesBackWithItsContents()
    {
        Recycle("BBB222", Path.Combine(Home, "photos"), null, Monday);
        var store = Store();

        store.Restore(store.List().Single());

        Assert.AreEqual("inside", File.ReadAllText(Path.Combine(Home, "photos", "inside.txt")));
    }

    [TestMethod]
    public void Restore_NeverOverwritesWhatHasTakenItsPlace()
    {
        var original = Path.Combine(Home, "report.pdf");
        Recycle("AAA111", original, "the deleted one", Monday);
        File.WriteAllText(original, "the newer one");
        var store = Store();

        var exception = Assert.ThrowsExactly<IOException>(() => store.Restore(store.List().Single()));

        StringAssert.Contains(exception.Message, "already exists");
        Assert.AreEqual("the newer one", File.ReadAllText(original));
        Assert.AreEqual(1, store.List().Count, "and the deleted one is still in the bin");
    }

    [TestMethod]
    public void Purge_DeletesTheContentAndTheRecord_AFolderWithEverythingInIt()
    {
        Recycle("AAA111", Path.Combine(Home, "report.pdf"), "pdf bytes", Monday);
        Recycle("BBB222", Path.Combine(Home, "photos"), null, Monday);
        File.SetAttributes(Path.Combine(Bin, "$RAAA111.pdf"), FileAttributes.ReadOnly);
        var store = Store();

        foreach (var item in store.List())
        {
            store.Purge(item);
        }

        Assert.AreEqual(0, Directory.GetFileSystemEntries(Bin).Length);
        Assert.IsFalse(File.Exists(Path.Combine(Home, "report.pdf")), "deleted for good is not restored");
    }
    #endregion

    #region The freedesktop trash
    [TestMethod]
    public void FreedesktopTrash_ListsRestoresAndPurges()
    {
        var trash = Path.Combine(_root, "Trash");
        Directory.CreateDirectory(Path.Combine(trash, "files"));
        Directory.CreateDirectory(Path.Combine(trash, "info"));
        var original = Path.Combine(Home, "my notes.txt");
        File.WriteAllText(Path.Combine(trash, "files", "my notes.txt"), "notes");
        File.WriteAllText(
            Path.Combine(trash, "info", "my notes.txt.trashinfo"),
            $"[Trash Info]\nPath={Uri.EscapeDataString(original).Replace("%2F", "/").Replace("%5C", "\\").Replace("%3A", ":")}\nDeletionDate=2026-10-05T09:30:00\n");
        File.WriteAllText(Path.Combine(trash, "files", "other.txt"), "other");
        File.WriteAllText(Path.Combine(trash, "info", "other.txt.trashinfo"), $"[Trash Info]\nPath={Path.Combine(Home, "other.txt")}\nDeletionDate=2026-10-05T10:00:00\n");
        var store = new FreedesktopTrashStore(trash);

        var items = store.List().OrderBy(i => i.Name).ToList();
        Assert.AreEqual(2, items.Count);
        Assert.AreEqual("my notes.txt", items[0].Name);
        Assert.AreEqual(original, items[0].OriginalPath, "the percent-encoded path is decoded");
        Assert.AreEqual(Monday, items[0].Deleted);
        Assert.AreEqual(5, items[0].Size);

        store.Restore(items[0]);
        store.Purge(items[1]);

        Assert.AreEqual("notes", File.ReadAllText(original));
        Assert.AreEqual(0, Directory.GetFiles(Path.Combine(trash, "files")).Length);
        Assert.AreEqual(0, Directory.GetFiles(Path.Combine(trash, "info")).Length);
    }

    [TestMethod]
    public void FreedesktopRecord_WithoutAPath_IsNotARecord()
    {
        Assert.IsNull(FreedesktopTrashStore.ParseRecord("[Trash Info]\nDeletionDate=2026-10-05T09:30:00\n"));
        Assert.AreEqual(("/home/me/a b.txt", Monday), FreedesktopTrashStore.ParseRecord("[Trash Info]\r\nPath=/home/me/a%20b.txt\r\nDeletionDate=2026-10-05T09:30:00\r\n"));
    }
    #endregion

    #region The service: the drive's listing and its file system
    [TestMethod]
    public async Task ListAsync_IsNewestFirst_WithUrlsThatAreTheNames()
    {
        Recycle("AAA111", Path.Combine(Home, "old.txt"), "old", Monday);
        Recycle("BBB222", Path.Combine(Home, "new.txt"), "newer", Monday.AddDays(1));
        Recycle("CCC333", Path.Combine(Home, "photos"), null, Monday.AddHours(1));

        var entries = await Service().ListAsync(CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "recycle://new.txt", "recycle://photos", "recycle://old.txt" }, entries.Select(e => e.Url.FullUrl).ToArray());
        Assert.IsTrue(entries.All(e => e.Url.PathType == PathType.File), "a deleted folder is one row, not a folder to walk");
        Assert.AreEqual("Folder", entries[1].FileSize.Trim());
        Assert.AreEqual("2026-10-06", entries[0].Date);
        Assert.AreEqual("recycle://", entries[0].Url.ParentUrl.FullUrl, "the panel is the folder a row is in");
        StringAssert.Contains(entries[0].GetDisplayFileSize(200), "Deleted from " + Home);
    }

    [TestMethod]
    public void Name_NumbersASecondFileOfTheSameName_TheSameWayEveryTime()
    {
        RecycledItem Item(string key, DateTime deleted) => new(key, "report.pdf", @"C:\a\report.pdf", deleted, 1, false, key);
        var first = Item("k1", Monday);
        var second = Item("k2", Monday.AddMinutes(1));
        var third = Item("k3", Monday.AddMinutes(2));

        var named = RecycleBinService.Name([third, first, second]);
        var again = RecycleBinService.Name([second, third, first]);

        CollectionAssert.AreEqual(new[] { ("report.pdf", first), ("report (2).pdf", second), ("report (3).pdf", third) }, named);
        CollectionAssert.AreEqual(named, again);
    }

    [TestMethod]
    public async Task RestoreAsync_RestoresWhatItCan_AndReportsWhatItCouldNot()
    {
        Recycle("AAA111", Path.Combine(Home, "a.txt"), "a", Monday);
        Recycle("BBB222", Path.Combine(Home, "b.txt"), "b", Monday);
        File.WriteAllText(Path.Combine(Home, "b.txt"), "in the way");
        var service = Service();
        await service.ListAsync(CancellationToken.None);

        var outcome = await service.RestoreAsync([new Url("recycle://a.txt"), new Url("recycle://b.txt"), new Url("recycle://never-listed.txt")]);

        Assert.AreEqual(1, outcome.Restored);
        Assert.AreEqual(2, outcome.Failures.Count);
        Assert.AreEqual("a", File.ReadAllText(Path.Combine(Home, "a.txt")));
        Assert.AreEqual("in the way", File.ReadAllText(Path.Combine(Home, "b.txt")));
        Assert.IsFalse(service.ExistsFile(new Url("recycle://a.txt")));
        Assert.IsTrue(service.ExistsFile(new Url("recycle://b.txt")));
    }

    [TestMethod]
    public async Task ADeletedFile_CanBeReadWhereItLies_AndDeletedForGood()
    {
        Recycle("AAA111", Path.Combine(Home, "a.txt"), "the content", Monday);
        Recycle("BBB222", Path.Combine(Home, "photos"), null, Monday);
        var service = Service();
        await service.ListAsync(CancellationToken.None);
        var file = new Url("recycle://a.txt");

        using var copy = new MemoryStream();
        await service.StreamToAsync(file, copy, null, CancellationToken.None);
        Assert.AreEqual("the content", Encoding.UTF8.GetString(copy.ToArray()));
        Assert.AreEqual("the content", Encoding.UTF8.GetString((await service.ReadFileAsync(file))!.Content!));

        var folderRefused = await Assert.ThrowsExactlyAsync<NotSupportedException>(
            () => service.StreamToAsync(new Url("recycle://photos"), new MemoryStream(), null, CancellationToken.None));
        StringAssert.Contains(folderRefused.Message, "restore it");

        Assert.IsTrue(service.DeleteFile(file, moveToRecycleBin: true));
        Assert.IsTrue(service.DeleteFile(new Url("recycle://photos"), moveToRecycleBin: false));
        Assert.IsFalse(service.DeleteFile(file, moveToRecycleBin: false), "already gone");
        Assert.AreEqual(0, Directory.GetFileSystemEntries(Bin).Length);
        Assert.IsFalse(File.Exists(Path.Combine(Home, "a.txt")));
    }

    [TestMethod]
    public void NothingIsCopiedIntoTheBin()
    {
        var exception = Assert.ThrowsExactly<NotSupportedException>(
            () => { _ = Service().StreamFromAsync(new Url("recycle://a.txt"), new MemoryStream(), null, CancellationToken.None); });

        StringAssert.Contains(exception.Message, "F8");
    }

    [TestMethod]
    public async Task ASystemWithoutAReadableBin_SaysSo()
    {
        var service = new RecycleBinService(new UnavailableRecycleBinStore("No bin here."));

        var exception = await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.ListAsync(CancellationToken.None));

        Assert.AreEqual("No bin here.", exception.Message);
    }

    [TestMethod]
    public void DescribeRestore_OneOrSeveral_WithAndWithoutFailures()
    {
        Url[] one = [new("recycle://a.txt")];
        Url[] three = [new("recycle://a.txt"), new("recycle://b.txt"), new("recycle://c.txt")];

        Assert.AreEqual("Restored a.txt", ContentModelRecycleDrive.Describe(one, new RestoreOutcome(1, [])));
        Assert.AreEqual("Restored 3 items", ContentModelRecycleDrive.Describe(three, new RestoreOutcome(3, [])));
        Assert.AreEqual("x already exists", ContentModelRecycleDrive.Describe(one, new RestoreOutcome(0, ["x already exists"])));
        Assert.AreEqual("Restored 2 of 3 — x already exists", ContentModelRecycleDrive.Describe(three, new RestoreOutcome(2, ["x already exists"])));
    }
    #endregion

    [TestMethod]
    public void TheScheme_ParsesAsItsOwnDrive()
    {
        var root = new Url(Container.RECYCLE);
        var row = new Url("recycle://report (2).pdf");

        Assert.AreEqual(ContainerType.Recycle, root.ContainerType);
        Assert.AreEqual(PathType.Folder, root.PathType);
        Assert.AreEqual(PathType.File, row.PathType);
        Assert.AreEqual("report (2).pdf", row.Name);
        Assert.AreEqual("pdf", row.Extension);
    }
}

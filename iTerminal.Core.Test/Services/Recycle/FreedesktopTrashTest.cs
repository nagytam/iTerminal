using iConsole.Data;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Recycle;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Services.Recycle;

/// <summary>
/// Putting files in the freedesktop Trash, in folders under the temp directory with the mount table
/// faked: nothing here reads or writes the Trash of the machine it runs on.
/// </summary>
[TestClass]
public class FreedesktopTrashTest
{
    private string _root = null!;

    private string HomeTrash => Path.Combine(_root, "share", "Trash");

    private string Work => Path.Combine(_root, "work");

    [TestInitialize]
    public void CreateSandbox()
    {
        _root = Directory.CreateTempSubdirectory("iTerminal.Trash.").FullName;
        Directory.CreateDirectory(Work);
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

    private FreedesktopTrash HomeOnly() => new(HomeTrash, () => [], 1000);

    private string Make(string relative, string content = "x")
    {
        var path = Path.Combine(Work, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [TestMethod]
    public void AFile_GoesToFiles_WithARecordOfWhereItWas()
    {
        var file = Make("my notes #1.txt", "notes");

        Assert.IsTrue(HomeOnly().TryMoveToTrash(file));

        Assert.IsFalse(File.Exists(file));
        Assert.AreEqual("notes", File.ReadAllText(Path.Combine(HomeTrash, "files", "my notes #1.txt")));
        var record = File.ReadAllText(Path.Combine(HomeTrash, "info", "my notes #1.txt.trashinfo"));
        StringAssert.StartsWith(record, "[Trash Info]\n");
        StringAssert.Contains(record, "my%20notes%20%231.txt");
        StringAssert.Contains(record, "DeletionDate=");
    }

    [TestMethod]
    public void AFolder_IsOneEntry_WithEverythingInIt()
    {
        Make(Path.Combine("proj", "src", "a.cs"));
        Make(Path.Combine("proj", "b.cs"));

        Assert.IsTrue(HomeOnly().TryMoveToTrash(Path.Combine(Work, "proj") + Path.DirectorySeparatorChar));

        Assert.IsFalse(Directory.Exists(Path.Combine(Work, "proj")));
        Assert.IsTrue(File.Exists(Path.Combine(HomeTrash, "files", "proj", "src", "a.cs")));
        Assert.IsTrue(File.Exists(Path.Combine(HomeTrash, "info", "proj.trashinfo")));
    }

    [TestMethod]
    public void TwoFilesOfOneName_DoNotCollide()
    {
        var trash = HomeOnly();
        trash.TryMoveToTrash(Make(Path.Combine("one", "a.txt"), "first"));
        trash.TryMoveToTrash(Make(Path.Combine("two", "a.txt"), "second"));

        Assert.AreEqual("first", File.ReadAllText(Path.Combine(HomeTrash, "files", "a.txt")));
        Assert.AreEqual("second", File.ReadAllText(Path.Combine(HomeTrash, "files", "a.txt.2")));
        Assert.IsTrue(File.Exists(Path.Combine(HomeTrash, "info", "a.txt.2.trashinfo")));
    }

    [TestMethod]
    public void AFileAlreadyInTheTrash_IsNotMovedAgain()
    {
        var trash = HomeOnly();
        trash.TryMoveToTrash(Make("a.txt"));
        var inside = Path.Combine(HomeTrash, "files", "a.txt");

        Assert.IsFalse(trash.TryMoveToTrash(inside));
        Assert.IsTrue(File.Exists(inside), "the caller deletes it for good; nothing was moved");
    }

    [TestMethod]
    public void TheStore_ListsWhatWasTrashed_AndRestoresIt()
    {
        var file = Make("report.txt", "q3");
        HomeOnly().TryMoveToTrash(file);
        var store = new FreedesktopTrashStore(HomeTrash);

        var item = store.List().Single();
        Assert.AreEqual("report.txt", item.Name);
        // Records always use '/'; on Windows (where this only runs as a test) that is not the native one.
        Assert.AreEqual(Path.GetFullPath(file).Replace('\\', '/'), item.OriginalPath.Replace('\\', '/'));
        Assert.AreEqual(2, item.Size);

        store.Restore(item with { OriginalPath = file });
        Assert.AreEqual("q3", File.ReadAllText(file));
        Assert.AreEqual(0, store.List().Count);
    }

    [TestMethod]
    public void AnotherVolume_GetsATrashOfItsOwn_WithPathsRelativeToItsRoot()
    {
        var volume = Path.Combine(_root, "usb");
        Directory.CreateDirectory(volume);
        var file = Path.Combine(volume, "photos", "cat.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "meow");
        var trash = new FreedesktopTrash(HomeTrash, () => [volume], 1000);

        Assert.IsTrue(trash.TryMoveToTrash(file));

        var volumeTrash = Path.Combine(volume, ".Trash-1000");
        Assert.IsTrue(File.Exists(Path.Combine(volumeTrash, "files", "cat.jpg")));
        StringAssert.Contains(File.ReadAllText(Path.Combine(volumeTrash, "info", "cat.jpg.trashinfo")), "Path=photos/cat.jpg\n");
        Assert.IsFalse(Directory.Exists(HomeTrash), "nothing crossed to the home trash");

        var store = new FreedesktopTrashStore(trash);
        var item = store.List().Single();
        Assert.AreEqual(file, item.OriginalPath, "the relative path is read against the volume's root");
        store.Restore(item);
        Assert.AreEqual("meow", File.ReadAllText(file));
    }

    [TestMethod]
    public void TopDirectoryOf_IsTheLongestMountPointThatContainsThePath()
    {
        var sep = Path.DirectorySeparatorChar.ToString();
        var rootMount = sep;
        var data = Path.Combine(_root, "mnt");
        var deep = Path.Combine(data, "deep");

        Assert.AreEqual(deep, FreedesktopTrash.TopDirectoryOf(Path.Combine(deep, "x", "y"), [rootMount, data, deep]));
        Assert.AreEqual(data, FreedesktopTrash.TopDirectoryOf(Path.Combine(data, "x"), [rootMount, data, deep]));
        Assert.AreEqual(sep, FreedesktopTrash.TopDirectoryOf(Path.Combine(_root, "other"), [rootMount, data]));
        Assert.AreEqual(sep, FreedesktopTrash.TopDirectoryOf(data + "-sibling", [data]), "a sibling that shares the prefix is not inside");
    }

    [TestMethod]
    public void MountInfo_UnescapesBlanksInMountPoints()
    {
        var points = FreedesktopTrash.ParseMountInfo(
        [
            "36 35 98:0 / / rw,noatime - ext4 /dev/sda1 rw",
            @"41 36 8:17 / /media/me/My\040Disk rw - vfat /dev/sdb1 rw",
        ]);

        CollectionAssert.AreEqual(new[] { "/", "/media/me/My Disk" }, points.ToArray());
    }

    [TestMethod]
    public void FileSystemService_SendsADeleteToTheTrash_OnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Where there is no Trash, a delete with the bin ticked goes to the Recycle Bin of the system.");
        }

        var file = Make("gone.txt");
        var service = new FileSystemService(HomeOnly());

        Assert.IsTrue(service.DeleteFile(new Url(file), moveToRecycleBin: true));

        Assert.IsFalse(File.Exists(file));
        Assert.IsTrue(File.Exists(Path.Combine(HomeTrash, "files", "gone.txt")));
    }

    [TestMethod]
    public void FileSystemService_DeletesForGood_WhenTheBinIsUnticked()
    {
        var file = Make("gone.txt");
        var service = new FileSystemService(HomeOnly());

        Assert.IsTrue(service.DeleteFile(new Url(file), moveToRecycleBin: false));

        Assert.IsFalse(File.Exists(file));
        Assert.IsFalse(Directory.Exists(HomeTrash));
    }
}

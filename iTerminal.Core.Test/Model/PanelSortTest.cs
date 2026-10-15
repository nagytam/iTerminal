using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Test.Model;

[TestClass]
public class PanelSortTest
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 30, 0);

    private static IEntry File(string name, long size, DateTime modified) =>
        new VirtualDriveEntry(new Url("docker://web/" + name), null, size.ToString(), modified) { Size = size };

    private static IEntry Folder(string name, DateTime modified) =>
        new VirtualDriveEntry(new Url("docker://web/" + name + "/"), null, "Folder", modified);

    private static IEntry Parent() => new VirtualDriveEntry(new Url("docker://"), "..", "Up");

    /// <summary>A listing in no particular order: two folders, four files, and the way out in the middle of it.</summary>
    private static List<IEntry> Listing() =>
    [
        File("notes.md", 700, Monday.AddDays(2)),
        Folder("zoo", Monday),
        File("Readme.txt", 40, Monday.AddDays(1)),
        Parent(),
        File("archive.zip", 9000, Monday),
        Folder("alpha", Monday.AddDays(3)),
        File("build.md", 40, Monday.AddDays(4)),
    ];

    private static string[] Names(PanelSort sort) => [.. sort.Apply(Listing()).Select(e => e.DisplayName)];

    [TestMethod]
    public void AsListed_LeavesTheDrivesOwnOrder()
    {
        var listing = Listing();

        Assert.AreSame(listing, PanelSort.AsListed.Apply(listing));
    }

    [TestMethod]
    public void ByName_ParentFirst_ThenFolders_ThenFiles_IgnoringCase()
    {
        CollectionAssert.AreEqual(
            new[] { "..", "alpha", "zoo", "archive.zip", "build.md", "notes.md", "Readme.txt" },
            Names(new PanelSort(PanelSortMode.Name)));
    }

    [TestMethod]
    public void Reversed_FoldersStillComeFirst_AndTheParentRowStillLeads()
    {
        CollectionAssert.AreEqual(
            new[] { "..", "zoo", "alpha", "Readme.txt", "notes.md", "build.md", "archive.zip" },
            Names(new PanelSort(PanelSortMode.Name, Descending: true)));
    }

    [TestMethod]
    public void BySize_FilesBySize_TiesAndFoldersByName()
    {
        CollectionAssert.AreEqual(
            new[] { "..", "alpha", "zoo", "build.md", "Readme.txt", "notes.md", "archive.zip" },
            Names(new PanelSort(PanelSortMode.Size)));

        // Largest first — and the two files of one size are still in name order, not reversed.
        CollectionAssert.AreEqual(
            new[] { "..", "alpha", "zoo", "archive.zip", "notes.md", "build.md", "Readme.txt" },
            Names(new PanelSort(PanelSortMode.Size, Descending: true)));
    }

    [TestMethod]
    public void ByExtension_ThenByName()
    {
        CollectionAssert.AreEqual(
            new[] { "..", "alpha", "zoo", "build.md", "notes.md", "Readme.txt", "archive.zip" },
            Names(new PanelSort(PanelSortMode.Extension)));
    }

    [TestMethod]
    public void ByDate_FoldersAmongThemselves_FilesAmongThemselves()
    {
        CollectionAssert.AreEqual(
            new[] { "..", "zoo", "alpha", "archive.zip", "Readme.txt", "notes.md", "build.md" },
            Names(new PanelSort(PanelSortMode.Date)));

        CollectionAssert.AreEqual(
            new[] { "..", "alpha", "zoo", "build.md", "notes.md", "Readme.txt", "archive.zip" },
            Names(new PanelSort(PanelSortMode.Date, Descending: true)));
    }

    #region The settings page's options
    [TestMethod]
    public void NaturalCompare_ReadsARunOfDigitsAsANumber()
    {
        string[] names = ["file10.txt", "File2.txt", "file1.txt", "file02.txt", "file.txt", "file100.txt", "a9", "a10b", "a10a"];

        Array.Sort(names, PanelSort.NaturalCompare);

        CollectionAssert.AreEqual(
            new[] { "a9", "a10a", "a10b", "file.txt", "file1.txt", "file02.txt", "File2.txt", "file10.txt", "file100.txt" },
            names);
        Assert.IsTrue(PanelSort.NaturalCompare("v99999999999999999999999", "v100000000000000000000000") < 0, "longer than any integer type");
        Assert.AreEqual(0, PanelSort.NaturalCompare("Same", "same"));
        Assert.IsTrue(PanelSort.NaturalCompare(null, "a") < 0);
    }

    private static string[] Numbered(PanelViewOptions options) =>
        [.. new PanelSort(PanelSortMode.Name).Apply([File("file10.txt", 1, Monday), Folder("zoo", Monday), File("file2.txt", 1, Monday)], options).Select(e => e.DisplayName)];

    [TestMethod]
    public void NaturalOrder_AndFoldersFirst_AreOptions()
    {
        CollectionAssert.AreEqual(new[] { "zoo", "file2.txt", "file10.txt" }, Numbered(PanelViewOptions.Default));
        CollectionAssert.AreEqual(new[] { "zoo", "file10.txt", "file2.txt" }, Numbered(PanelViewOptions.Default with { NaturalOrder = false }));
        CollectionAssert.AreEqual(new[] { "file2.txt", "file10.txt", "zoo" }, Numbered(PanelViewOptions.Default with { FoldersFirst = false }));
    }

    [TestMethod]
    public void HiddenEntries_AreLeftOut_InAnyOrder_ButNeverTheWayOut()
    {
        var folder = Directory.CreateTempSubdirectory("iTerminal.Hidden.").FullName;
        try
        {
            // A folder that is itself hidden, so the ".." row of what is inside it points at a hidden entry.
            var inside = Directory.CreateDirectory(Path.Combine(folder, ".inside")).FullName;
            System.IO.File.SetAttributes(inside, System.IO.File.GetAttributes(inside) | FileAttributes.Hidden);
            System.IO.File.WriteAllText(Path.Combine(inside, "seen.txt"), "x");
            var secret = Path.Combine(inside, ".secret");
            System.IO.File.WriteAllText(secret, "x");
            System.IO.File.SetAttributes(secret, System.IO.File.GetAttributes(secret) | FileAttributes.Hidden);

            List<IEntry> listing = [new FileSystemEntry("..", "file://" + inside + Path.DirectorySeparatorChar)];
            listing.AddRange(new DirectoryInfo(inside).EnumerateFileSystemInfos().Select(info => (IEntry)new FileSystemEntry(info)));
            Assert.IsTrue(listing[0].Url.IsHidden, "the parent row's folder is hidden");
            var hideHidden = PanelViewOptions.Default with { ShowHidden = false };

            CollectionAssert.AreEquivalent(new[] { "..", "seen.txt", ".secret" }, PanelSort.AsListed.Apply(listing, PanelViewOptions.Default).Select(e => e.DisplayName).ToArray());
            CollectionAssert.AreEqual(new[] { "..", "seen.txt" }, PanelSort.AsListed.Apply(listing, hideHidden).Select(e => e.DisplayName).ToArray());
            CollectionAssert.AreEqual(new[] { "..", "seen.txt" }, new PanelSort(PanelSortMode.Size, true).Apply(listing, hideHidden).Select(e => e.DisplayName).ToArray());
        }
        finally
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
            {
                System.IO.File.SetAttributes(path, FileAttributes.Normal);
            }
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void ViewOptions_AreKeptInTheSettings_AndDefaultToWhatThePanelsAlwaysDid()
    {
        var settings = new iTerminal.Core.Services.SettingsService();
        Assert.AreEqual(new PanelViewOptions(true, true, true), PanelViewOptions.Load(settings));
        Assert.AreEqual(PanelViewOptions.Default, PanelViewOptions.Load(null));

        new PanelViewOptions(ShowHidden: false, FoldersFirst: true, NaturalOrder: false).Save(settings);

        Assert.AreEqual(new PanelViewOptions(false, true, false), PanelViewOptions.Load(settings));
    }
    #endregion

    [TestMethod]
    public void Serialize_AndParse_RoundTrip_AndNonsenseIsTheDrivesOrder()
    {
        foreach (var mode in Enum.GetValues<PanelSortMode>())
        {
            foreach (var descending in new[] { false, true })
            {
                var sort = new PanelSort(mode, descending);
                Assert.AreEqual(sort, PanelSort.Parse(sort.Serialize()));
            }
        }

        Assert.AreEqual("Size,desc", new PanelSort(PanelSortMode.Size, true).Serialize());
        Assert.AreEqual(PanelSort.AsListed, PanelSort.Parse(null));
        Assert.AreEqual(PanelSort.AsListed, PanelSort.Parse("Colour"));
        Assert.AreEqual(PanelSort.AsListed, PanelSort.Parse("42"));
        Assert.AreEqual(new PanelSort(PanelSortMode.Date), PanelSort.Parse(" date "));
    }

    [TestMethod]
    public void Describe_IsForTheFooter_AndSaysNothingForTheDrivesOrder()
    {
        Assert.AreEqual("size ↓", new PanelSort(PanelSortMode.Size, true).Describe());
        Assert.AreEqual("name ↑", new PanelSort(PanelSortMode.Name).Describe());
        Assert.AreEqual(string.Empty, PanelSort.AsListed.Describe());
    }
}

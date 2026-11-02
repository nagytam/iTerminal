using iConsole.Data;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model.ContentModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Test.Controls.Contents;

/// <summary>
/// The Info listing's rows for one entry. The labels are passed through as their keys, so the rows show which
/// field each value is.
/// </summary>
[TestClass]
public class ListingInfoTest
{
    private static string Key(string key) => key;

    [TestMethod]
    public void NoEntry_IsTheSingleNoEntryRow()
    {
        var rows = ListingInfo.Describe(null, Key);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("Listing.NoEntry", rows[0].Value);
    }

    [TestMethod]
    public void DotDot_IsTheNoEntryRow()
    {
        var rows = ListingInfo.Describe(new FileSystemEntry("..", Path.GetTempPath()), Key);

        Assert.AreEqual(1, rows.Count, "the '..' row is not an entry the panel can describe");
        Assert.AreEqual("Listing.NoEntry", rows[0].Value);
    }

    [TestMethod]
    public void File_ListsNameTypePathSizeModifiedAndAttributes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"listing-info-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "hello");
        try
        {
            var rows = ListingInfo.Describe(new FileSystemEntry(new FileInfo(path)), Key);

            Assert.AreEqual(Path.GetFileName(path), Find(rows, "Listing.Name"));
            Assert.AreEqual("Listing.TypeFile", Find(rows, "Listing.Type"));
            Assert.AreEqual(path, Find(rows, "Listing.Path"));
            Assert.IsFalse(string.IsNullOrEmpty(Find(rows, "Listing.Size")), "the size comes from the Url");
            Assert.AreNotEqual("Listing.Unknown", Find(rows, "Listing.Attributes"), "a real file carries attributes");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Folder_IsTypedAsAFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"listing-info-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var rows = ListingInfo.Describe(new FileSystemEntry(new DirectoryInfo(folder)), Key);

            Assert.AreEqual("Listing.TypeFolder", Find(rows, "Listing.Type"));
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [TestMethod]
    public void PathString_ReportsUnknownAttributes()
    {
        var rows = ListingInfo.Describe(new FileSystemEntry(Path.GetTempPath()), Key);

        Assert.AreEqual("Listing.Unknown", Find(rows, "Listing.Attributes"), "a Url made from a path string carries no attributes");
    }

    private static string Find(IReadOnlyList<ListingInfoRow> rows, string label) =>
        rows.Single(r => r.Label == label).Value;
}

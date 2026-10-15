using iTerminal.Core.Services.Search;

namespace iTerminal.Core.Test.Services.Search;

/// <summary>The F7 walk over a small known tree.</summary>
[TestClass]
public class FileSearchTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateTree()
    {
        _root = Path.Combine(Path.GetTempPath(), "filesearch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "docs", "old"));
        Directory.CreateDirectory(Path.Combine(_root, ".git", "objects"));
        Directory.CreateDirectory(Path.Combine(_root, "report folder"));
        File.WriteAllText(Path.Combine(_root, "readme.md"), "# Title\nThe quick brown fox\n");
        File.WriteAllText(Path.Combine(_root, "docs", "2024-report-final.txt"), "line one\nline two mentions Invoice 42\n");
        File.WriteAllText(Path.Combine(_root, "docs", "old", "notes.md"), "nothing here\n");
        File.WriteAllText(Path.Combine(_root, ".git", "objects", "report.pack"), "invoice\n");
        File.WriteAllBytes(Path.Combine(_root, "docs", "image.bin"), [0x89, 0x50, 0x00, 0x00, (byte)'i', (byte)'n', (byte)'v', (byte)'o', (byte)'i', (byte)'c', (byte)'e']);
    }

    [TestCleanup]
    public void DeleteTree()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    private List<FileSearchHit> Search(string names, string text = "", bool subfolders = true)
    {
        List<FileSearchHit> hits = [];
        FileSearch.Run(new FileSearchQuery(_root, names, text, subfolders), hits.Add, null, CancellationToken.None);
        return hits;
    }

    private List<string> Relative(IEnumerable<FileSearchHit> hits) =>
        [.. hits.Select(h => Path.GetRelativePath(_root, h.Path).Replace('\\', '/'))];

    [TestMethod]
    public void AWildcardPatternMatchesAcrossTheTree()
    {
        CollectionAssert.AreEquivalent(new[] { "readme.md", "docs/old/notes.md" }, Relative(Search("*.md")));
    }

    [TestMethod]
    public void SeveralPatternsAreSeparatedBySemicolonsOrCommas()
    {
        CollectionAssert.AreEquivalent(new[] { "readme.md", "docs/old/notes.md", "docs/2024-report-final.txt" }, Relative(Search("*.md; *.TXT")));
        CollectionAssert.AreEquivalent(new[] { "readme.md", "docs/old/notes.md", "docs/2024-report-final.txt" }, Relative(Search("*.md,*.txt")));
    }

    [TestMethod]
    public void APlainWordMatchesAnywhereInANameFoldersIncluded()
    {
        var hits = Search("report");

        CollectionAssert.AreEquivalent(new[] { "docs/2024-report-final.txt", "report folder" }, Relative(hits));
        Assert.IsTrue(hits.Single(h => h.Path.EndsWith("report folder", StringComparison.Ordinal)).IsFolder);
    }

    [TestMethod]
    public void VersionControlInternalsAreNotSearched()
    {
        Assert.IsFalse(Relative(Search("*")).Any(p => p.StartsWith(".git/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ContentIsMatchedCaseInsensitivelyWithTheLineItIsOn()
    {
        var hit = Search("", "invoice").Single();

        Assert.AreEqual("docs/2024-report-final.txt", Relative([hit]).Single());
        Assert.AreEqual(2, hit.LineNumber);
        Assert.AreEqual("line two mentions Invoice 42", hit.LineText);
    }

    [TestMethod]
    public void ABinaryFileIsNeverAContentMatch()
    {
        Assert.IsFalse(Search("*.bin", "invoice").Any());
    }

    [TestMethod]
    public void WithoutSubfoldersOnlyTheStartFolderIsSearched()
    {
        CollectionAssert.AreEquivalent(new[] { "readme.md" }, Relative(Search("*.md", subfolders: false)));
    }

    [TestMethod]
    public void ProgressCountsWhatWasWalked()
    {
        List<FileSearchProgress> reports = [];

        var final = FileSearch.Run(new FileSearchQuery(_root, "*.md"), _ => { }, reports.Add, CancellationToken.None);

        Assert.AreEqual(2, final.Hits);
        Assert.AreEqual(4, final.FoldersScanned, "root, docs, docs/old, report folder — and not .git");
        Assert.IsTrue(final.FilesScanned >= 4);
        Assert.IsTrue(reports.Count >= 4, "a report per folder");
    }

    [TestMethod]
    public void ACancelledSearchStops()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            FileSearch.Run(new FileSearchQuery(_root, "*"), _ => { }, null, cancellation.Token));
    }

    [TestMethod]
    public void AMissingFolderSaysSo()
    {
        Assert.ThrowsExactly<DirectoryNotFoundException>(() =>
            FileSearch.Run(new FileSearchQuery(Path.Combine(_root, "nope"), "*"), _ => { }, null, CancellationToken.None));
    }
}

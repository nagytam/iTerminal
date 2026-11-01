using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Model.ContentModels;

/// <summary>
/// Marking rows in a panel (<see cref="IMarkableContentModel"/>, implemented once in
/// <see cref="ContentModelBase"/>): what F5, Shift+F6 and F8 act on when there is more than one.
/// </summary>
[TestClass]
public class ContentModelMarksTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "ContentModelMarksTest_" + Guid.NewGuid().ToString("N")[..8]);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private MarkableTestModel CreateModel() => MarkableTestModel.Create(_root);

    private static string[] MarkedNames(IMarkableContentModel model) =>
        [.. model.GetMarkedEntries().Select(e => e.DisplayName)];

    private static IEntry Row(MarkableTestModel model, string name) => model.Row(name);

    [Timeout(60_000)]
    [TestMethod]
    public void ToggleMarksAndUnmarks()
    {
        var model = CreateModel();

        Assert.IsTrue(model.ToggleMark(Row(model, "b.txt")));
        Assert.IsTrue(model.IsMarked(Row(model, "b.txt")));
        Assert.AreEqual(1, model.MarkedCount);

        model.ToggleMark(Row(model, "b.txt"));
        Assert.AreEqual(0, model.MarkedCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheParentRowIsNeverMarked()
    {
        var model = CreateModel();

        Assert.IsFalse(model.ToggleMark(Row(model, "..")));
        Assert.AreEqual(0, model.MarkedCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MarkedEntriesComeInListingOrder()
    {
        var model = CreateModel();
        model.ToggleMark(Row(model, "c.jpg"));
        model.ToggleMark(Row(model, "a.txt"));

        CollectionAssert.AreEqual(new[] { "a.txt", "c.jpg" }, MarkedNames(model));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AMarkedEntryThatLeftTheListingIsNotReported()
    {
        var model = CreateModel();
        model.ToggleMark(Row(model, "a.txt"));
        model.ToggleMark(Row(model, "b.txt"));

        model.Entries.ReplaceAll([.. model.Entries.AsSnapshot().Where(e => e.DisplayName != "a.txt")]);

        CollectionAssert.AreEqual(new[] { "b.txt" }, MarkedNames(model));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NavigatingElsewhereDropsTheMarks()
    {
        // F8 must never act on rows of a folder the user has left.
        var model = CreateModel();
        model.ToggleMark(Row(model, "a.txt"));

        model.SetUrl(new Url(Path.Combine(_root, "photos") + Path.DirectorySeparatorChar));
        model.SetUrl(new Url(_root + Path.DirectorySeparatorChar));

        Assert.AreEqual(0, model.MarkedCount, "coming back does not bring them back either");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APatternMarksMatchingFilesButNotFolders()
    {
        var model = CreateModel();

        var changed = model.SetMarks("*.txt;photo", marked: true, includeFolders: false);

        Assert.AreEqual(2, changed);
        CollectionAssert.AreEqual(new[] { "a.txt", "b.txt" }, MarkedNames(model));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APatternCanTakeFoldersToo()
    {
        var model = CreateModel();

        model.SetMarks("photo", marked: true, includeFolders: true);

        CollectionAssert.AreEqual(new[] { "photos" }, MarkedNames(model), "a bare word matches names containing it");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APatternUnmarks()
    {
        var model = CreateModel();
        model.SetMarks("*", marked: true, includeFolders: true);

        model.SetMarks("*.txt", marked: false, includeFolders: false);

        CollectionAssert.AreEqual(new[] { "photos", "c.jpg" }, MarkedNames(model));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InvertingLeavesFoldersAlone()
    {
        var model = CreateModel();
        model.ToggleMark(Row(model, "a.txt"));

        model.InvertMarks(includeFolders: false);

        CollectionAssert.AreEqual(new[] { "b.txt", "c.jpg" }, MarkedNames(model));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheFooterCountsTheMarksAndTheirSize()
    {
        var model = CreateModel();
        model.ToggleMark(Row(model, "a.txt"));
        model.ToggleMark(Row(model, "b.txt"));

        StringAssert.Contains(model.GetFooter(), "2 marked, 6");
    }
}

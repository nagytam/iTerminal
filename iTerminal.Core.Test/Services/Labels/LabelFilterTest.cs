using iConsole.Data;
using iTerminal.Core.Services.Labels;
using iTerminal.Core.Test.Helper;
using Moq;

namespace iTerminal.Core.Test.Services.Labels;

[TestClass]
public class LabelFilterTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "LabelFilterTest_" + Guid.NewGuid().ToString("N")[..8]);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheColourAndTheTagAreTested()
    {
        var red = new FileLabel(LabelColour.Red, ["invoice"]);
        var plain = FileLabel.None;

        Assert.IsTrue(new LabelFilter(LabelColour.Red, null).Matches(red));
        Assert.IsFalse(new LabelFilter(LabelColour.Blue, null).Matches(red));
        Assert.IsTrue(new LabelFilter(LabelColour.None, null).Matches(plain));
        Assert.IsFalse(new LabelFilter(LabelColour.None, null).Matches(red));
        Assert.IsTrue(new LabelFilter(null, "INVOICE").Matches(red), "tags match ignoring case");
        Assert.IsFalse(new LabelFilter(LabelColour.Red, "2026").Matches(red), "both must match");
        Assert.IsTrue(new LabelFilter(null, null).IsEmpty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Describe_SaysWhatIsShown()
    {
        Assert.AreEqual("label: red, tag invoice", new LabelFilter(LabelColour.Red, "invoice").Describe());
        Assert.AreEqual("label: no colour", new LabelFilter(LabelColour.None, null).Describe());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFilteredPanel_ShowsTheMatchingFiles_AndKeepsTheParentAndFolders()
    {
        Directory.CreateDirectory(_root);
        var model = MarkableTestModel.Create(_root);
        var labels = LabelsFor(new Dictionary<string, FileLabel>
        {
            [Path.Combine(_root, "a.txt")] = new(LabelColour.Red, []),
        });

        model.SetEntryFilter(new LabelFilter(LabelColour.Red, null).ToEntryFilter(labels), "label: red");

        CollectionAssert.AreEqual(new[] { "..", "photos", "a.txt" }, model.FilteredEntries.Select(e => e.DisplayName).ToList());
        Assert.AreEqual("label: red", model.EntryFilterDescription);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ClearingTheFilter_ShowsEverythingAgain()
    {
        Directory.CreateDirectory(_root);
        var model = MarkableTestModel.Create(_root);
        model.SetEntryFilter(new LabelFilter(LabelColour.Red, null).ToEntryFilter(LabelsFor(new Dictionary<string, FileLabel>())), "label: red");

        model.SetEntryFilter(null, null);

        Assert.AreEqual(5, model.FilteredEntries.Count);
        Assert.IsNull(model.EntryFilterDescription);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARelabelledFile_AppearsOnlyAfterTheFilterIsRefreshed()
    {
        Directory.CreateDirectory(_root);
        var model = MarkableTestModel.Create(_root);
        var labelled = new Dictionary<string, FileLabel>();
        model.SetEntryFilter(new LabelFilter(LabelColour.Red, null).ToEntryFilter(LabelsFor(labelled)), "label: red");
        Assert.AreEqual(2, model.FilteredEntries.Count, "the parent row and the folder");

        labelled[Path.Combine(_root, "b.txt")] = new(LabelColour.Red, []);
        model.RefreshEntryFilter();

        Assert.IsTrue(model.FilteredEntries.Any(e => e.DisplayName == "b.txt"));
    }

    private static IFileLabelService LabelsFor(IDictionary<string, FileLabel> labels)
    {
        Mock<IFileLabelService> service = new();
        service.Setup(s => s.Get(It.IsAny<string>())).Returns<string>(path =>
            labels.TryGetValue(Path.GetFullPath(path), out var label) ? label : FileLabel.None);
        return service.Object;
    }
}

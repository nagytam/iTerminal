using System.ComponentModel;
using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Model.ContentModels;
using Moq;

namespace iTerminal.Core.Test.Model.ContentModels;

[TestClass]
public class ContentModelBaseTest
{
    private sealed class TestEntry : IEntry
    {
        public TestEntry(string name, string fullUrl)
        {
            Name = name;
            DisplayName = name;
            FullName = name;
            DirectoryName = Path.GetDirectoryName(new Uri(fullUrl).LocalPath) ?? string.Empty;
            Url = new Url(fullUrl);
        }

        public string FileSize => "1";
        public string Date => "2025-01-01";
        public string Time => "00:00";
        public string DisplayName { get; }
        public string Name { get; }
        public string FullName { get; }
        public Url Url { get; }
        public string DirectoryName { get; }
        public string? NumberOfFiles { get; set; }
        public bool Exists => true;
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public string GetDisplayName(int width) => DisplayName.PadRight(width);
        public string GetDisplayFileSize(int width) => FileSize.PadRight(width);
        public ConxelString FormatItemName(int width, bool cursor, ColorScheme colorScheme, VisualState visualState)
            => new(Name.PadRight(width), colorScheme.Foreground, colorScheme.Background);
    }

    private sealed class TestContentModel : ContentModelBase
    {
        public TestContentModel(IFrequentExecutionGuardService frequentExecutionGuardService)
            : base("Test", frequentExecutionGuardService)
        {
            Url = new Url(Directory.GetCurrentDirectory());
            ContentBrowser = Mock.Of<IContentBrowser>();
        }

        public override bool IsReadOnly => false;
        public override string GetFooter() => "footer";
        public override string GetHeader(int width) => new string('H', width);
        public override void ReadUrl() { }
        public override void Refresh(bool forceRefresh = false) { }
        public override void SetUrl(Url path) => Url = path;
        public string EntryCountFooterForTest() => EntryCountFooter();
    }

    private static TestContentModel CreateModel()
    {
        Mock<IFrequentExecutionGuardService> guard = new();
        guard.Setup(service => service.Execute(It.IsAny<Action>())).Callback<Action>(action => action());
        return new TestContentModel(guard.Object);
    }

    /// <summary>The count used to be joined to its word by a border character: "══5═entries══".</summary>
    [TestMethod]
    public void TheEntryCountIsFramedBySpaces()
    {
        var model = CreateModel();
        model.Entries.Add(new TestEntry("alpha", "file:///alpha"));
        model.Entries.Add(new TestEntry("beta", "file:///beta"));

        Assert.AreEqual(" 2 entries ", model.EntryCountFooterForTest());
    }

    [TestMethod]
    public void OneEntryIsSingular()
    {
        var model = CreateModel();
        model.Entries.Add(new TestEntry("alpha", "file:///alpha"));

        Assert.AreEqual(" 1 entry ", model.EntryCountFooterForTest());
    }

    [TestMethod]
    public void TheFilterFollowsTheCount()
    {
        var model = CreateModel();
        model.Entries.Add(new TestEntry("alpha", "file:///alpha"));
        model.Entries.Add(new TestEntry("beta", "file:///beta"));
        model.Filter = "al";

        StringAssert.EndsWith(model.EntryCountFooterForTest(), "Filter: al ");
    }

    [TestMethod]
    public void RememberFocusedEntryAndRestoreFocusedEntry_RestoresSelectionByUrl()
    {
        Mock<IFrequentExecutionGuardService> guard = new();
        guard.Setup(service => service.Execute(It.IsAny<Action>())).Callback<Action>(action => action());
        TestContentModel model = new(guard.Object)
        {
            ViewSize = new Size(10, 3),
            ScrollableSize = new Size(10, 3),
        };
        TestEntry alpha = new("alpha", "file:///alpha");
        TestEntry beta = new("beta", "file:///beta");
        TestEntry gamma = new("gamma", "file:///gamma");
        model.Entries.Add(alpha);
        model.Entries.Add(beta);
        model.Entries.Add(gamma);
        model.CursorPosition.Y = 1;

        model.RememberFocusedEntry();
        model.Entries.ReplaceAll([gamma, alpha, beta]);
        bool restored = model.RestoreFocusedEntry();

        Assert.IsTrue(restored);
        Assert.AreEqual(2, model.CursorPosition.Y);
    }

    [TestMethod]
    public void Update_ClampsPositionsAndNormalizesEmptyFilter()
    {
        Mock<IFrequentExecutionGuardService> guard = new();
        int executionCount = 0;
        guard.Setup(service => service.Execute(It.IsAny<Action>())).Callback<Action>(action =>
        {
            executionCount++;
            action();
        });
        TestContentModel model = new(guard.Object)
        {
            CursorPosition = new Position(0, 10),
            ScrollPosition = new Position(0, 10),
        };
        model.Entries.Add(new TestEntry("alpha", "file:///alpha"));
        model.Entries.Add(new TestEntry("beta", "file:///beta"));
        model.Filter = string.Empty;

        model.Update();

        Assert.IsNull(model.Filter);
        Assert.AreEqual(1, model.CursorPosition.Y);
        Assert.AreEqual(1, model.ScrollPosition.Y);
        Assert.AreEqual(2, model.ScrollableSize.Height);
        Assert.AreEqual(1, executionCount);
    }
}

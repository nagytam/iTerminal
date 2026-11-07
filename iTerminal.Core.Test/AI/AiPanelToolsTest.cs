using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.AI;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.AI;

[TestClass]
public class AiPanelToolsTest
{
    private sealed class Fixture
    {
        public Mock<IContentModel> Left { get; } = new();
        public Mock<IContentModel> Right { get; } = new();
        public Mock<IMarkableContentModel> LeftMarks { get; }
        public Mock<ISortableContentModel> LeftSort { get; }
        public Mock<IContentModelService> Service { get; } = new();
        public AiPanelTools Tools { get; }
        public List<IEntry> LeftEntries { get; } = [];

        public Fixture()
        {
            Left.Setup(m => m.Name).Returns(Content.Left);
            Right.Setup(m => m.Name).Returns(Content.Right);
            Left.Setup(m => m.Url).Returns(new Url(Path.GetTempPath()));
            Right.Setup(m => m.Url).Returns(new Url(Path.GetTempPath()));
            Left.Setup(m => m.FilteredEntries).Returns(LeftEntries);
            LeftMarks = Left.As<IMarkableContentModel>();
            LeftMarks.Setup(m => m.CanMark).Returns(true);
            LeftSort = Left.As<ISortableContentModel>();
            LeftSort.Setup(m => m.CanSort).Returns(true);
            LeftSort.Setup(m => m.Sort).Returns(PanelSort.AsListed);

            Service.Setup(s => s.ContentModels).Returns([Left.Object, Right.Object]);
            Service.Setup(s => s.GetFocusedModel()).Returns(Left.Object);
            Tools = new AiPanelTools(Service.Object);
        }

        public IEntry AddEntry(string name, bool folder = false)
        {
            var entry = new Mock<IEntry>();
            entry.Setup(e => e.DisplayName).Returns(name);
            entry.Setup(e => e.Name).Returns(name);
            entry.Setup(e => e.FileSize).Returns("1 KB");
            string path = Path.Combine(Path.GetTempPath(), name) + (folder ? Path.DirectorySeparatorChar : string.Empty);
            entry.Setup(e => e.Url).Returns(new Url(path));
            LeftEntries.Add(entry.Object);
            return entry.Object;
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Create_ToolNames_AreUniqueAndCoverThePanelFunctions()
    {
        var names = new AiPanelTools(Mock.Of<IContentModelService>()).Create().Select(t => t.Name).ToList();

        Assert.AreEqual(names.Count, names.Distinct().Count());
        foreach (string expected in new[] { "get_panel_state", "list_panel_entries", "mark_entries", "set_panel_sort", "set_panel_filter", "copy_to_other_panel", "move_to_other_panel", "delete_entries", "create_folder", "rename_entry" })
        {
            CollectionAssert.Contains(names, expected);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ResolvePanel_LeftRightFocusedOther()
    {
        var f = new Fixture();

        Assert.AreSame(f.Left.Object, f.Tools.ResolvePanel("left"));
        Assert.AreSame(f.Right.Object, f.Tools.ResolvePanel("RIGHT"));
        Assert.AreSame(f.Left.Object, f.Tools.ResolvePanel(null));
        Assert.AreSame(f.Right.Object, f.Tools.ResolvePanel("other"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ListPanelEntries_ShowsCursorMarksAndFolders()
    {
        var f = new Fixture();
        IEntry a = f.AddEntry("a.txt");
        IEntry dir = f.AddEntry("docs", folder: true);
        f.Left.Setup(m => m.GetFocusedEntry()).Returns(dir);
        f.LeftMarks.Setup(m => m.IsMarked(a)).Returns(true);

        string text = f.Tools.ListPanelEntries("left");

        StringAssert.Contains(text, " * a.txt");
        StringAssert.Contains(text, ">  docs/");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ListPanelEntries_CapsAtMax()
    {
        var f = new Fixture();
        for (int i = 0; i < 5; i++)
        {
            f.AddEntry($"f{i}.txt");
        }

        string text = f.Tools.ListPanelEntries("left", max: 2);

        StringAssert.Contains(text, "3 more not shown");
        Assert.IsFalse(text.Contains("f4.txt"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UnknownPanel_ReportsAnErrorInsteadOfThrowing()
    {
        var f = new Fixture();
        f.Service.Setup(s => s.GetFocusedModel()).Returns((IContentModel?)null);
        f.Service.Setup(s => s.GetLastContentModel()).Returns((IContentModel?)null);

        StringAssert.StartsWith(f.Tools.GetPanelState(), "Error:");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MarkEntries_PassesPatternsToTheModel()
    {
        var f = new Fixture();
        f.LeftMarks.Setup(m => m.SetMarks("*.cs", true, false)).Returns(3);
        f.LeftMarks.Setup(m => m.MarkedCount).Returns(3);

        string text = f.Tools.MarkEntries("*.cs", "left");

        StringAssert.Contains(text, "Marked 3");
        f.LeftMarks.Verify(m => m.SetMarks("*.cs", true, false), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetPanelSort_ParsesModeAndDirection_AndRejectsGarbage()
    {
        var f = new Fixture();

        f.Tools.SetPanelSort("size", "left", descending: true);
        f.LeftSort.Verify(m => m.SetSort(new PanelSort(PanelSortMode.Size, true)), Times.Once);

        f.Tools.SetPanelSort("as_listed", "left", descending: true);
        f.LeftSort.Verify(m => m.SetSort(new PanelSort(PanelSortMode.AsListed, false)), Times.Once);

        StringAssert.StartsWith(f.Tools.SetPanelSort("colour", "left"), "Error:");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MoveCursorToEntry_FindsByPrefix_AndReportsAmbiguity()
    {
        var f = new Fixture();
        f.AddEntry("alpha.txt");
        f.AddEntry("alpine.txt");
        f.AddEntry("beta.txt");
        var cursor = new Position(0, 0);
        f.Left.Setup(m => m.CursorPosition).Returns(cursor);

        StringAssert.Contains(f.Tools.MoveCursorToEntry("al", "left"), "ambiguous");
        f.Tools.MoveCursorToEntry("bet", "left");

        Assert.AreEqual(2, cursor.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DeleteEntries_WithoutConfirmation_DeletesNothing()
    {
        var f = new Fixture();
        f.AddEntry("keep.txt");

        string text = f.Tools.DeleteEntries("left", "keep.txt", confirmed: false);

        StringAssert.Contains(text, "Not deleted");
        f.Left.Verify(m => m.Delete(It.IsAny<Url>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DeleteEntries_Confirmed_MovesNamedEntryToTheRecycleBin()
    {
        var f = new Fixture();
        f.AddEntry("gone.txt");

        string text = f.Tools.DeleteEntries("left", "gone.txt", confirmed: true);

        StringAssert.Contains(text, "Deleted");
        f.Left.Verify(m => m.Delete(It.IsAny<Url>(), false, true), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CopyToOtherPanel_RefusesToOverwrite()
    {
        var f = new Fixture();
        f.AddEntry("same.txt");
        f.Right.Setup(m => m.ExistsFile(It.IsAny<Url>())).Returns(true);

        string text = await f.Tools.CopyToOtherPanel("left", "same.txt");

        StringAssert.Contains(text, "already exists");
        f.Left.Verify(m => m.CopyAsync(It.IsAny<Url>(), It.IsAny<Url>(), It.IsAny<bool>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CreateFolder_RejectsPathSeparators()
    {
        var f = new Fixture();

        StringAssert.StartsWith(await f.Tools.CreateFolder("../x", "left"), "Error:");
        f.Left.Verify(m => m.CreateFolderAsync(It.IsAny<Url>()), Times.Never);
    }
}

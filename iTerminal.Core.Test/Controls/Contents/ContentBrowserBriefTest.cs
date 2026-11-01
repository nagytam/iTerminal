using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iConsole.Test.Core;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Preview;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Controls.Contents;

/// <summary>
/// The Brief listing: several name columns, filled row-major, names only. Checked on the cells the panel
/// paints, not on the model.
/// </summary>
[TestClass]
public class ContentBrowserBriefTest
{
    // 80 columns is 78 inside the border: 78 / 16 = 4 columns of 19, the last one 21 wide.
    private const int PanelWidth = 80;
    // Four rows inside the border gives two visible rows of entries once the header and footer rows are taken.
    private const int PanelHeight = 4;

    private sealed class TestContentModel(IFrequentExecutionGuardService frequentExecutionGuardService)
        : ContentModelBase("Panel", frequentExecutionGuardService)
    {
        public override bool IsReadOnly => true;

        public override string GetFooter() => string.Empty;

        public override string GetHeader(int width) => Name;

        public override void ReadUrl()
        {
        }

        public override void Refresh(bool forceRefresh = false)
        {
        }

        public override void SetUrl(Url url)
        {
            Url = url;
        }
    }

    private static TestContentModel CreateModel()
    {
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(action => action());
        var model = new TestContentModel(guard.Object)
        {
            ViewSize = new Size(PanelWidth - 2, PanelHeight - 2)
        };
        model.SetUrl(new Url(Path.GetTempPath()));
        return model;
    }

    private static ContentBrowser CreateBrowser(TestContentModel model)
    {
        var contentModelService = new Mock<IContentModelService>().Object;
        var taskManager = new Mock<TaskManager>(MockBehavior.Loose).Object;
        var terminalModel = new Mock<TerminalModel>(MockBehavior.Loose, taskManager, contentModelService).Object;

        return new ContentBrowser("Panel", contentModelService, terminalModel, taskManager, UiTestHelper.TestTheme())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(PanelWidth, PanelHeight),
            ContentModel = model,
        };
    }

    private static void ShowEntries(TestContentModel model, int count)
    {
        var folder = new Url(Path.GetTempPath());
        model.Entries.ReplaceAll(Enumerable.Range(0, count)
            .Select(i => (IEntry)new VirtualDriveEntry(folder, $"e{i}", "Up"))
            .ToList());
        model.Update();
    }

    [TestMethod]
    public void Brief_SevenEntries_FourColumns_FilledRowMajor_NamesOnly()
    {
        var model = CreateModel();
        var sut = CreateBrowser(model);
        ShowEntries(model, 7);
        sut.ToggleBriefListing();
        var screen = new ScreenBuffer(PanelWidth, PanelHeight);

        sut.Draw(screen);
        sut.Draw(screen);

        // Row 0 of the entries is line 0: e0..e3. Cells start at x=1, 20, 39 and 58.
        var row0 = screen.Line(1);
        Assert.AreEqual("e0", row0.Substring(1, 2), $"first cell:{Environment.NewLine}{screen}");
        Assert.AreEqual("e1", row0.Substring(20, 2), $"second cell:{Environment.NewLine}{screen}");
        Assert.AreEqual("e2", row0.Substring(39, 2), $"third cell:{Environment.NewLine}{screen}");
        Assert.AreEqual("e3", row0.Substring(58, 2), $"last cell:{Environment.NewLine}{screen}");

        // Row 1 is line 1: e4, e5, e6 and an empty last cell.
        var row1 = screen.Line(2);
        Assert.AreEqual("e4", row1.Substring(1, 2), $"{Environment.NewLine}{screen}");
        Assert.AreEqual("e5", row1.Substring(20, 2), $"{Environment.NewLine}{screen}");
        Assert.AreEqual("e6", row1.Substring(39, 2), $"{Environment.NewLine}{screen}");
        Assert.AreEqual(' ', row1[58], $"the empty cell must be blank:{Environment.NewLine}{screen}");

        // Names only: no size or date text on either row.
        Assert.IsFalse(row0.Contains("2026-"), "a date column leaked into the Brief row");
        Assert.IsFalse(row0.Contains(":"), "a time column leaked into the Brief row");
    }

    [TestMethod]
    public void Brief_ColumnCount_FollowsTheWidth_WithAMinimumCellOfSixteen()
    {
        var model = CreateModel();
        var sut = CreateBrowser(model);
        sut.ToggleBriefListing();

        Assert.AreEqual(4, ((iConsole.Model.IColumnScrollableModel)model).ColumnCount, "width 80: 78 / 16 = 4");

        sut.ViewSize = new Size(40, PanelHeight);
        sut.ApplyListingLayout();
        Assert.AreEqual(2, ((iConsole.Model.IColumnScrollableModel)model).ColumnCount, "width 40: 38 / 16 = 2");

        sut.ViewSize = new Size(20, PanelHeight);
        sut.ApplyListingLayout();
        Assert.AreEqual(1, ((iConsole.Model.IColumnScrollableModel)model).ColumnCount, "width 20: at least one column");
    }

    [TestMethod]
    public void Toggling_BackToLong_RestoresOneColumn()
    {
        var model = CreateModel();
        var sut = CreateBrowser(model);
        ShowEntries(model, 7);

        sut.ToggleBriefListing();
        sut.ToggleBriefListing();

        Assert.IsFalse(sut.IsBriefListing);
        Assert.AreEqual(1, ((iConsole.Model.IColumnScrollableModel)model).ColumnCount);
    }

    [TestMethod]
    public void Toggle_OnThePreviewPanel_DoesNothing()
    {
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(action => action());
        var preview = new ContentModelPreview("RightPanel", null, Mock.Of<IContentModelService>(), new FilePreviewService(), guard.Object)
        {
            ViewSize = new Size(PanelWidth - 2, PanelHeight - 2)
        };
        var sut = CreateBrowser(CreateModel());
        sut.ContentModel = preview;

        sut.ToggleBriefListing();

        Assert.IsFalse(sut.IsBriefListing, "the Preview panel does not offer Brief");
    }

    [TestMethod]
    public void MoveWithinLine_StaysInTheLine_AndStopsAtItsEnds()
    {
        var model = CreateModel();
        var sut = CreateBrowser(model);
        ShowEntries(model, 7);
        sut.ToggleBriefListing();

        sut.MoveWithinLine(1);
        Assert.AreEqual(1, model.CursorPosition.Y, "right moves one entry");

        sut.MoveWithinLine(-1);
        sut.MoveWithinLine(-1);
        Assert.AreEqual(0, model.CursorPosition.Y, "left at the first column stays on its line");

        model.CursorPosition.Y = 3;
        sut.MoveWithinLine(1);
        Assert.AreEqual(3, model.CursorPosition.Y, "right at the line's last column does not wrap to the next row");
    }

    [TestMethod]
    public void MoveWithinLine_DoesNothingInLong()
    {
        var model = CreateModel();
        var sut = CreateBrowser(model);
        ShowEntries(model, 7);

        sut.MoveWithinLine(1);

        Assert.AreEqual(0, model.CursorPosition.Y);
    }
}

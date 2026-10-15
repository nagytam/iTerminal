using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iConsole.Test.Core;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Controls.Contents;

/// <summary>
/// What a panel actually paints, cell by cell. The renderer never clears a layer between frames, so
/// a row that skips cells leaves the previous frame's text in them — which is how the undated '..'
/// row of a tool-backed drive (docker://, git://, archive://) came to show the date of the folder
/// the panel had just left, shifted by one cell.
/// </summary>
[TestClass]
public class ContentBrowserRenderTest
{
    private const int PanelWidth = 60;
    private const int PanelHeight = 6;

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

    private static void ShowEntries(TestContentModel model, params IEntry[] entries)
    {
        model.Entries.ReplaceAll(entries.ToList());
        model.Update();
    }

    [TestMethod]
    public void Draw_UndatedParentRowAfterADatedOne_LeavesNoStaleDateOrTime()
    {
        var folder = new Url(Path.GetTempPath());
        var model = CreateModel();
        var sut = CreateBrowser(model);
        var screen = new ScreenBuffer(PanelWidth, PanelHeight);

        // A folder whose '..' row carries a date, as a local folder's does…
        ShowEntries(model,
            new VirtualDriveEntry(folder, "..", "Up", new DateTime(2026, 10, 5, 18, 58, 0)),
            new VirtualDriveEntry(folder, "web", "Up", new DateTime(2026, 10, 4, 8, 0, 0)));
        sut.Draw(screen);
        UiTestHelper.AssertScreenContains(screen, "2026-10-05│18:58", "the dated row should draw its date first");

        // …then a drive whose '..' row has none, drawn over the same layer.
        ShowEntries(model,
            new VirtualDriveEntry(folder, "..", "Up"),
            new VirtualDriveEntry(folder, "web", "Up", new DateTime(2026, 10, 4, 8, 0, 0)));
        sut.Draw(screen);

        var parentRow = screen.Line(1);
        Assert.IsFalse(parentRow.Contains("026-10-05"), $"stale date left on the '..' row:{Environment.NewLine}{screen}");
        Assert.IsFalse(parentRow.Contains("18:58"), $"stale time left on the '..' row:{Environment.NewLine}{screen}");

        // The row keeps its columns: the separators sit where the dated row's do.
        var datedRow = screen.Line(2);
        Assert.AreEqual(datedRow.LastIndexOf('│'), parentRow.LastIndexOf('│'), $"the time column moved:{Environment.NewLine}{screen}");
        for (var x = 1; x < PanelWidth - 1; x++)
        {
            Assert.IsTrue(screen.IsWritten(x, 1), $"cell {x} of the '..' row was not painted:{Environment.NewLine}{screen}");
        }
    }

    [TestMethod]
    public void Draw_UndatedRow_PaintsTheWholeRowInTheCursorColour()
    {
        var folder = new Url(Path.GetTempPath());
        var model = CreateModel();
        var sut = CreateBrowser(model);
        var screen = new ScreenBuffer(PanelWidth, PanelHeight);

        ShowEntries(model, new VirtualDriveEntry(folder, "..", "Up"));
        sut.Draw(screen);

        // The cursor bar runs to the panel's right edge, not to where the size column ends.
        var background = screen.BackgroundAt(1, 1);
        for (var x = 1; x < PanelWidth - 1; x++)
        {
            Assert.AreEqual(background, screen.BackgroundAt(x, 1), $"cell {x} of the cursor row:{Environment.NewLine}{screen}");
        }
    }
}

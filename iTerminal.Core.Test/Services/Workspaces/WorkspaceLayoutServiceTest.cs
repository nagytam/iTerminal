using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Model;
using iTerminal.Core.Model.Workspaces;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Workspaces;
using Moq;

namespace iTerminal.Core.Test.Services.Workspaces;

/// <summary>What a workspace records: where each panel is, how it is sorted, the bottom bar and the panel with the cursor.</summary>
[TestClass]
public class WorkspaceLayoutServiceTest
{
    private static Mock<IContentModel> Panel(string name, string path, PanelSort? sort = null)
    {
        Mock<IContentModel> model = new();
        model.Setup(m => m.Name).Returns(name);
        model.Setup(m => m.Url).Returns(new Url(path));
        if (sort is { } s)
        {
            Mock<ISortableContentModel> sortable = model.As<ISortableContentModel>();
            sortable.Setup(x => x.Sort).Returns(s);
        }

        return model;
    }

    private static (WorkspaceLayoutService Service, Mock<IContentModelService> Models, Mock<ISettingsService> Settings) Create(params Mock<IContentModel>[] panels)
    {
        Mock<IContentModelService> models = new();
        ConcurrentList<IContentModel> list = [];
        foreach (Mock<IContentModel> panel in panels)
        {
            list.Add(panel.Object);
        }

        models.Setup(m => m.ContentModels).Returns(list);
        Mock<ISettingsService> settings = new();
        settings.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);
        WorkspaceLayoutService service = new(models.Object, settings.Object, Mock.Of<IServiceProvider>());
        return (service, models, settings);
    }

    [TestMethod]
    public void Capture_RecordsBothPanelsUrlsAndTheirSorts()
    {
        string leftPath = Path.Combine(Path.GetTempPath(), "left");
        string rightPath = Path.Combine(Path.GetTempPath(), "right");
        var (service, _, _) = Create(
            Panel(ContentModelService.Left, leftPath, new PanelSort(PanelSortMode.Size, Descending: true)),
            Panel(ContentModelService.Right, rightPath, PanelSort.AsListed));

        WorkspaceLayout layout = service.Capture();

        Assert.AreEqual(new Url(leftPath).FullUrl, layout.LeftUrl);
        Assert.AreEqual(new Url(rightPath).FullUrl, layout.RightUrl);
        Assert.AreEqual(new PanelSort(PanelSortMode.Size, Descending: true).Serialize(), layout.LeftSort);
        Assert.AreEqual(PanelSort.AsListed.Serialize(), layout.RightSort);
    }

    [TestMethod]
    public void Capture_ASortThatCannotBeReadFromThePanelComesFromItsSetting()
    {
        // A panel that is not sortable (a playing queue, a preview) keeps the sort its setting holds.
        var (service, _, settings) = Create(Panel(ContentModelService.Left, Path.GetTempPath()));
        settings.Setup(s => s.Get(PanelSort.SettingsKey(ContentModelService.Left))).Returns("Extension,desc");

        WorkspaceLayout layout = service.Capture();

        Assert.AreEqual(new PanelSort(PanelSortMode.Extension, Descending: true).Serialize(), layout.LeftSort);
    }

    [TestMethod]
    public void Capture_FocusIsTheRightPanelWhenItWasLastUsed()
    {
        var left = Panel(ContentModelService.Left, Path.GetTempPath());
        var right = Panel(ContentModelService.Right, Path.GetTempPath());
        var (service, models, _) = Create(left, right);
        models.Setup(m => m.GetLastContentModel()).Returns(right.Object);

        Assert.AreEqual(ContentModelService.Right, service.Capture().FocusedPanel);
    }

    [TestMethod]
    public void Capture_FocusDefaultsToTheLeftPanel()
    {
        var (service, models, _) = Create(Panel(ContentModelService.Left, Path.GetTempPath()), Panel(ContentModelService.Right, Path.GetTempPath()));
        models.Setup(m => m.GetLastContentModel()).Returns((IContentModel?)null);

        Assert.AreEqual(ContentModelService.Left, service.Capture().FocusedPanel);
    }

    [TestMethod]
    public void Capture_WithNoPanelsOpenRecordsEmptyUrlsAndTheAiBar()
    {
        var (service, _, _) = Create();

        WorkspaceLayout layout = service.Capture();

        Assert.AreEqual(string.Empty, layout.LeftUrl);
        Assert.AreEqual(string.Empty, layout.RightUrl);
        Assert.AreEqual(WorkspaceBar.Ai, layout.Bar);
        Assert.AreEqual(ContentModelService.Left, layout.FocusedPanel);
    }
}

using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Commands;
using Moq;

namespace iTerminal.Core.Test.Commands;

/// <summary>The Menu key command: bound to the key on a panel, keyless in the Edit menu, and only for a row that can be acted on.</summary>
[TestClass]
public class PanelContextMenuCommandTest
{
    private static Mock<IContentModel> ModelOnRow(string name)
    {
        var entry = new Mock<IEntry>();
        entry.SetupGet(e => e.DisplayName).Returns(name);
        entry.SetupGet(e => e.Url).Returns(new Url(Path.Combine(Path.GetTempPath(), name)));
        var model = new Mock<IContentModel>();
        model.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        return model;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThePanelsCommand_IsBoundToTheMenuKey_AndTheEditMenusIsNot()
    {
        var service = new Mock<IContentModelService>();

        var forMenu = new PanelContextMenuCommand(service.Object);

        Assert.IsNull(forMenu.Key);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheEditMenusCommand_WorksOnTheLastPanel_WhenNothingIsFocused()
    {
        var model = ModelOnRow("report.txt");
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetFocusedModel()).Returns((IContentModel?)null);
        service.Setup(s => s.GetLastContentModel()).Returns(model.Object);

        Assert.IsTrue(new PanelContextMenuCommand(service.Object).CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NothingToActOn_MeansItDeclines()
    {
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetFocusedModel()).Returns((IContentModel?)null);
        service.Setup(s => s.GetLastContentModel()).Returns((IContentModel?)null);

        Assert.IsFalse(new PanelContextMenuCommand(service.Object).CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheParentRow_IsDeclined()
    {
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetFocusedModel()).Returns(ModelOnRow("..").Object);

        Assert.IsFalse(new PanelContextMenuCommand(service.Object).CanExecute());
    }
}

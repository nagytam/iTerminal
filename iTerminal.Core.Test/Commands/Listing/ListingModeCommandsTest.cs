using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iConsole.Test.Core;
using iTerminal.Core.Commands.Listing;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Preview;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Commands.Listing;

[TestClass]
public class ListingModeCommandsTest
{
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

    private static Mock<IFrequentExecutionGuardService> ImmediateGuard()
    {
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(action => action());
        return guard;
    }

    private static (ContentBrowser Browser, TestContentModel Model) CreateBrowser(Mock<IContentModelService> service)
    {
        var model = new TestContentModel(ImmediateGuard().Object)
        {
            ViewSize = new Size(78, 2)
        };
        model.SetUrl(new Url(Path.GetTempPath()));
        var taskManager = new Mock<TaskManager>(MockBehavior.Loose).Object;
        var terminalModel = new Mock<TerminalModel>(MockBehavior.Loose, taskManager, service.Object).Object;
        var browser = new ContentBrowser("Panel", service.Object, terminalModel, taskManager, UiTestHelper.TestTheme())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(80, 4),
            ContentModel = model,
        };
        return (browser, model);
    }

    [TestMethod]
    public void Toggle_CanRun_OnlyWhenItsOwnModelIsFocused()
    {
        var service = new Mock<IContentModelService>();
        var (browser, model) = CreateBrowser(service);
        var command = new ToggleListingModeCommand(service.Object, browser);

        service.Setup(s => s.GetFocusedModel()).Returns(model);
        Assert.IsTrue(command.CanExecute(), "the focused panel offers Brief");

        service.Setup(s => s.GetFocusedModel()).Returns(Mock.Of<IContentModel>());
        Assert.IsFalse(command.CanExecute(), "the other panel must not toggle this one");
    }

    [TestMethod]
    public void Toggle_RunsOnTheFocusedPanel_AndSwitchesTheMode()
    {
        var service = new Mock<IContentModelService>();
        var (browser, model) = CreateBrowser(service);
        service.Setup(s => s.GetFocusedModel()).Returns(model);
        var command = new ToggleListingModeCommand(service.Object, browser);

        command.ExecuteAsync(null).Wait();

        Assert.IsTrue(browser.IsBriefListing);
    }

    [TestMethod]
    public void Toggle_CannotRun_OnThePreviewPanel()
    {
        var service = new Mock<IContentModelService>();
        var (browser, _) = CreateBrowser(service);
        var preview = new ContentModelPreview("RightPanel", null, Mock.Of<IContentModelService>(), new FilePreviewService(), ImmediateGuard().Object)
        {
            ViewSize = new Size(78, 2)
        };
        browser.ContentModel = preview;
        service.Setup(s => s.GetFocusedModel()).Returns(preview);
        var command = new ToggleListingModeCommand(service.Object, browser);

        Assert.IsFalse(command.CanExecute());
    }

    [TestMethod]
    public void LeftAndRight_AreClaimed_OnlyInBrief()
    {
        var service = new Mock<IContentModelService>();
        var (browser, _) = CreateBrowser(service);
        var left = new ListingModeLeftCommand(browser);
        var right = new ListingModeRightCommand(browser);

        Assert.IsFalse(left.CanExecute(), "in the one-column listing the arrows stay free");
        Assert.IsFalse(right.CanExecute());

        browser.ToggleBriefListing();

        Assert.IsTrue(left.CanExecute());
        Assert.IsTrue(right.CanExecute());
    }
}

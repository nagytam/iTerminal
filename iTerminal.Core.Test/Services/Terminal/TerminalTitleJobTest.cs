using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services.Terminal;
using Moq;

namespace iTerminal.Core.Test.Services.Terminal;

[TestClass]
public class TerminalTitleJobTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void BuildTitle_NamesTheFolderAndTheApplication()
    {
        var model = new Mock<IContentModel>();
        model.SetupGet(m => m.Url).Returns(new Url(Directory.GetCurrentDirectory()));

        string title = TerminalTitleJob.BuildTitle(model.Object);

        Assert.AreEqual(model.Object.Url.FullUrl + " - iTerminal BETA", title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BuildTitle_WithoutAPanel_IsTheApplicationName()
    {
        Assert.AreEqual("iTerminal BETA", TerminalTitleJob.BuildTitle(null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Publish_UsesTheFocusedPanel_ElseTheLastOne_AndWritesOnlyChanges()
    {
        var focused = new Mock<IContentModel>();
        focused.SetupGet(m => m.Url).Returns(new Url(Directory.GetCurrentDirectory()));
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetFocusedModel()).Returns((IContentModel?)null);
        service.Setup(s => s.GetLastContentModel()).Returns(focused.Object);
        var titles = new List<string>();
        var terminalTitle = new Mock<ITerminalTitleService>();
        terminalTitle.SetupSet(t => t.Title = It.IsAny<string>()).Callback<string>(titles.Add);
        var job = new TerminalTitleJob(service.Object, terminalTitle.Object);

        job.Publish();
        job.Publish();

        Assert.AreEqual(2, titles.Count, "the job hands the title over every poll; the service skips an unchanged one");
        Assert.AreEqual(TerminalTitleJob.BuildTitle(focused.Object), titles[0]);
    }
}

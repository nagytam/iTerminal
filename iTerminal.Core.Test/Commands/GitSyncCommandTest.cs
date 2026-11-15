using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs.Git;
using iTerminal.Core.Services.Git;
using iConsole.Data;
using iConsole.Display;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Commands.Test;

/// <summary>Git sync (Ctrl+Shift+G) opens the sync dialog, and only inside a working tree.</summary>
[TestClass]
public class GitSyncCommandTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "iTerminal-gitsync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "repo", ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "plain"));
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    private GitSyncCommand Create(string folder)
    {
        Mock<IContentModel> model = new();
        model.SetupGet(m => m.Url).Returns(new Url(folder));
        Mock<IContentModelService> service = new();
        service.Setup(s => s.GetFocusedModel()).Returns(model.Object);
        Mock<IThemeService> theme = new();
        theme.SetupGet(t => t.CurrentColorScheme).Returns(new ColorScheme());
        GitSyncDialog dialog = new(Mock.Of<IGitService>(), service.Object, theme.Object);
        return new GitSyncCommand(dialog, service.Object);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_OnlyInsideAWorkingTree()
    {
        Assert.IsTrue(Create(Path.Combine(_root, "repo")).CanExecute());
        Assert.IsFalse(Create(Path.Combine(_root, "plain")).CanExecute());
    }
}

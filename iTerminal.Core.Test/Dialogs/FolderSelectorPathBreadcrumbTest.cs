using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>The Path page shows the typed path as clickable crumbs; a click cuts the box back to that folder.</summary>
[TestClass]
public class FolderSelectorPathBreadcrumbTest
{
    [TestMethod]
    public void SplitPath_WindowsPath_KeepsTheDriveRoot()
    {
        var parts = FolderSelectorPathTabPage.SplitPath(@"C:\Users\me\docs");

        CollectionAssert.AreEqual(new[] { "C:", "Users", "me", "docs" }, parts.Select(p => p.Name).ToArray());
        CollectionAssert.AreEqual(new[] { @"C:\", @"C:\Users", @"C:\Users\me", @"C:\Users\me\docs" }, parts.Select(p => p.Prefix).ToArray());
    }

    [TestMethod]
    public void SplitPath_UnixPath_StartsAtTheRoot()
    {
        var parts = FolderSelectorPathTabPage.SplitPath("/home/me/");

        CollectionAssert.AreEqual(new[] { "/", "home", "me" }, parts.Select(p => p.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "/", "/home", "/home/me" }, parts.Select(p => p.Prefix).ToArray());
    }

    [TestMethod]
    public void SplitPath_Url_StartsAtTheHost()
    {
        var parts = FolderSelectorPathTabPage.SplitPath("ssh://me@host/var/log");

        CollectionAssert.AreEqual(new[] { "ssh://me@host", "var", "log" }, parts.Select(p => p.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "ssh://me@host", "ssh://me@host/var", "ssh://me@host/var/log" }, parts.Select(p => p.Prefix).ToArray());
    }

    [TestMethod]
    public void SplitPath_EmptyOrBlank_HasNoCrumbs()
    {
        Assert.AreEqual(0, FolderSelectorPathTabPage.SplitPath(string.Empty).Count);
        Assert.AreEqual(0, FolderSelectorPathTabPage.SplitPath("   ").Count);
    }

    [TestMethod]
    public void SplitPath_RelativeName_IsOneCrumb()
    {
        var parts = FolderSelectorPathTabPage.SplitPath("notes");

        Assert.AreEqual(1, parts.Count);
        Assert.AreEqual("notes", parts[0].Prefix);
    }

    [TestMethod]
    public async Task ClickingACrumbCutsTheBoxBackToThatFolder()
    {
        var harness = new FolderSelectorTestHarness();
        var page = new FolderSelectorPathTabPage(
            harness.ContentModelService,
            harness.FolderHistoryService,
            harness.BeepService,
            harness.DateTimeProviderService,
            harness.SettingsService,
            harness.ThemeService)
        {
            ViewSize = new Size(80, 20),
        };
        await page.OnInitializeAsync();
        page.TextBoxPath.TextModel.Lines[0] = "/home/me/docs";
        page.TextBoxPath.TextModel.TriggerPropertyChanged();

        // "/ › home › me › docs": the "home" crumb starts at column 4 of the crumb row (page column 1 + 4).
        await page.HandleMouseEventAsync(new MouseEvent { Position = new Position(page.AbsolutePosition.X + 1 + 4, page.AbsolutePosition.Y + 2), LeftClicked = true });

        Assert.AreEqual("/home", page.TextBoxPath.Text);
    }
}

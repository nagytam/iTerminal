using iConsole.Data;
using iTerminal.Core.Model;
using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Model;

[TestClass]
public class FolderPathCompletionModelTest
{
    private static (FolderPathCompletionModel model, ISettingsService settings, FolderHistoryService history) Create()
    {
        var settingsService = new SettingsService();
        var folderHistoryService = new FolderHistoryService(settingsService);
        return (new FolderPathCompletionModel(settingsService, folderHistoryService), settingsService, folderHistoryService);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Completions_IncludeTheFoldersThePanelsHaveVisited()
    {
        var (model, _, history) = Create();
        history.Add(new Url(@"C:\projects"));

        CollectionAssert.Contains(model.Completions.ToList(), new Url(@"C:\projects").FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Completions_FollowTheHistoryAsItGrows()
    {
        // The history changes while the dialog is open; a snapshot taken at construction would go
        // stale the first time a panel moved.
        var (model, _, history) = Create();
        Assert.AreEqual(0, model.Completions.Count());

        history.Add(new Url(@"C:\later"));

        Assert.AreEqual(1, model.Completions.Count());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InsertCompletion_PersistsUnderTheModelsOwnKey()
    {
        var (model, settings, _) = Create();

        model.InsertCompletion(@"smb://server/share/");

        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(
            settings.Get(FolderPathCompletionModel.SettingsKey)!, "smb://server/share/");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InsertCompletion_DoesNotPersistTheHistory()
    {
        // Copying the history into the settings key would duplicate it on disk and freeze it at
        // whatever it was the first time the Path page was opened.
        var (model, settings, history) = Create();
        history.Add(new Url(@"C:\projects"));

        model.InsertCompletion(@"smb://server/share/");

        Assert.IsFalse(settings.Get(FolderPathCompletionModel.SettingsKey)!.Contains("projects"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Completions_ListATypedPathOnlyOnceEvenAfterVisitingIt()
    {
        var (model, _, history) = Create();
        model.InsertCompletion(@"smb://server/share/");
        history.Add(new Url(@"smb://server/share/"));

        Assert.AreEqual(1, model.Completions.Count(c => c == @"smb://server/share/"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UpdateMatchingValues_KeepsWhatContainsTheTypedText()
    {
        var (model, _, history) = Create();
        history.Add(new Url(@"C:\projects\crypton"));
        history.Add(new Url(@"C:\windows"));

        model.UpdateMatchingValues("crypt");

        Assert.AreEqual(1, model.MatchingValues.Count);
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(model.MatchingValues[0], "crypton");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UpdateMatchingValues_IgnoresCase()
    {
        var (model, _, history) = Create();
        history.Add(new Url(@"C:\Projects"));

        model.UpdateMatchingValues("projects");

        Assert.AreEqual(1, model.MatchingValues.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UpdateMatchingValues_ReplacesThePreviousMatches()
    {
        var (model, _, history) = Create();
        history.Add(new Url(@"C:\projects"));
        model.UpdateMatchingValues("projects");

        model.UpdateMatchingValues("nothing-matches-this");

        Assert.AreEqual(0, model.MatchingValues.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_WithoutAHistory_StillCompletesWhatWasTyped()
    {
        var model = new FolderPathCompletionModel(new SettingsService(), null);

        model.InsertCompletion(@"memdrive://");

        CollectionAssert.Contains(model.Completions.ToList(), @"memdrive://");
    }
}

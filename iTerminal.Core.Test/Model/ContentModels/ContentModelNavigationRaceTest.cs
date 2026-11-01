using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Model.ContentModels;

/// <summary>
/// The cursor restore is asked for on the key handler (a session restored, a folder left, a search
/// hit revealed) while listings land on a worker. Both used to read and write the same three things
/// — the pending restore, the remembered row and the cursor — unguarded, and a listing landing in
/// the instant a restore was applied put the cursor back on "..". End to end that was
/// <c>F7FindsFilesAndCtrlPListsThemInThePanel</c> failing one run in six.
/// </summary>
[TestClass]
public class ContentModelNavigationRaceTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize() =>
        _root = Path.Combine(Path.GetTempPath(), "ContentModelNavigationRaceTest_" + Guid.NewGuid().ToString("N")[..8]);

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    [TestMethod]
    [Timeout(60_000)]
    public void ARestoreAndALandingListingNeverLeaveTheCursorOnTheParentRow()
    {
        var model = MarkableTestModel.Create(_root);
        var listing = model.FilteredEntries.ToList();
        var target = model.Row("b.txt").Url.FullUrl;
        var expected = listing.FindIndex(e => e.Url.FullUrl == target);
        var failures = 0;

        for (var round = 0; round < 2_000; round++)
        {
            model.CursorPosition.Y = 0;
            model.RememberFocusedEntry();
            using var start = new Barrier(2);

            var restore = Task.Run(() =>
            {
                start.SignalAndWait();
                model.RestoreNavigation(target, 0, 0);
            });
            var landing = Task.Run(() =>
            {
                start.SignalAndWait();
                model.ReplaceEntriesAndRestoreFocus(listing, isComplete: true);
            });
            Task.WaitAll(restore, landing);

            if (model.CursorPosition.Y != expected)
            {
                failures++;
            }
        }

        Assert.AreEqual(0, failures, "rounds that left the cursor somewhere other than the restored row");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ACachedListingDoesNotReplaceAFreshOneThatLandedFirst()
    {
        // Two refresh tasks for one folder: the faster one's enumeration lands, then the slower one's
        // cache lookup — a copy of the folder from the last run, without the file added since.
        var model = MarkableTestModel.Create(_root);
        var fresh = model.FilteredEntries.ToList();
        var stale = fresh.Skip(1).ToList();
        model.ReplaceEntriesAndRestoreFocus(fresh, isComplete: true);

        model.ReplaceEntriesAndRestoreFocus(stale, isComplete: true, isFromCache: true);

        Assert.AreEqual(fresh.Count, model.FilteredEntries.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ACachedListingStillLandsBeforeAnyFreshOne()
    {
        var model = MarkableTestModel.Create(_root);
        var cached = model.FilteredEntries.Skip(1).ToList();
        model.ResetNavigation();

        model.ReplaceEntriesAndRestoreFocus(cached, isComplete: true, isFromCache: true);

        Assert.AreEqual(cached.Count, model.FilteredEntries.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARestoreWaitsOutAStreamedBatchThatDoesNotHoldItsRow()
    {
        // A big folder arrives in batches of 50; the hit being revealed is in a later one. Settling
        // for the fallback index on the first batch put the cursor on ".." for good.
        var model = MarkableTestModel.Create(_root);
        var complete = model.FilteredEntries.ToList();
        var target = model.Row("c.jpg").Url.FullUrl;
        var batch = complete.Where(e => e.Url.FullUrl != target).ToList();

        model.ReplaceEntriesAndRestoreFocus(batch, isComplete: false);
        model.RestoreNavigation(target, 0, 0);
        Assert.AreEqual(0, model.CursorPosition.Y, "nothing to land on yet");

        model.ReplaceEntriesAndRestoreFocus(complete, isComplete: true);

        Assert.AreEqual(target, model.GetFocusedEntry()?.Url.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARestoreWhoseRowIsGoneFromACompleteListingFallsBackToItsIndex()
    {
        var model = MarkableTestModel.Create(_root);
        var complete = model.FilteredEntries.ToList();
        model.ReplaceEntriesAndRestoreFocus(complete, isComplete: true);

        model.RestoreNavigation(new Url(Path.Combine(_root, "deleted.txt")).FullUrl, 3, 0);

        Assert.AreEqual(3, model.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARefreshKeepsTheCursorOnItsRowWhenRowsAreAddedAbove()
    {
        var model = MarkableTestModel.Create(_root);
        var listing = model.FilteredEntries.ToList();
        model.CursorPosition.Y = 3; // b.txt
        var newcomer = MarkableTestModel.Create(_root + "_x").Row("a.txt");
        try
        {
            model.ReplaceEntriesAndRestoreFocus([listing[0], newcomer, .. listing.Skip(1)], isComplete: true);

            Assert.AreEqual("b.txt", model.GetFocusedEntry()?.DisplayName);
        }
        finally
        {
            Directory.Delete(_root + "_x", recursive: true);
        }
    }
}

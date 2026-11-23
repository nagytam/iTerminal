using iConsole.Model;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Model;

[TestClass]
public class DirectoryCompletionModelTest
{
    private sealed class FakeLister(Dictionary<string, DirectoryChild[]> folders) : IDirectoryLister
    {
        public List<string> Listed { get; } = [];
        public List<int> ThreadIds { get; } = [];
        public Func<string, IReadOnlyList<DirectoryChild>>? Override { get; set; }

        public IReadOnlyList<DirectoryChild> List(string folder, int max)
        {
            lock (Listed)
            {
                Listed.Add(folder);
                ThreadIds.Add(Environment.CurrentManagedThreadId);
            }

            if (Override is not null)
            {
                return Override(folder);
            }

            return folders.TryGetValue(folder, out DirectoryChild[]? children) ? children : [];
        }
    }

    private static FakeLister UsersFolder() => new(new Dictionary<string, DirectoryChild[]>
    {
        [@"C:\Users\"] =
        [
            new("tamas", true), new("Tara", true), new("Public", true), new("desktop.ini", false), new("notes.txt", false), new("disk.iso", false),
        ],
    });

    private static List<string> Match(DirectoryCompletionModel model, string text)
    {
        model.UpdateMatchingValues(text);
        return [.. model.MatchingValues];
    }

    [TestMethod]
    public void AFolderIsOfferedUnderTheTextTypedSoFar_EndingInTheSeparatorTyped()
    {
        var model = new DirectoryCompletionModel(UsersFolder());

        CollectionAssert.AreEqual(new[] { @"C:\Users\tamas\", @"C:\Users\Tara\" }, Match(model, @"C:\Users\ta"));
    }

    [TestMethod]
    public void TheSeparatorAndCaseUpToTheLastSegmentAreKeptAsTyped()
    {
        FakeLister lister = new(new Dictionary<string, DirectoryChild[]> { ["c:/users/"] = [new("Tamas", true)] });
        var model = new DirectoryCompletionModel(lister);

        CollectionAssert.AreEqual(new[] { "c:/users/Tamas/" }, Match(model, "c:/users/TA"));
    }

    [TestMethod]
    public void FoldersOnly_LeavesFilesOut_AndFilesAndFoldersListsFoldersFirst()
    {
        FakeLister lister = UsersFolder();

        CollectionAssert.AreEqual(
            new[] { @"C:\Users\Public\", @"C:\Users\tamas\", @"C:\Users\Tara\" },
            Match(new DirectoryCompletionModel(lister), @"C:\Users\"));
        CollectionAssert.AreEqual(
            new[] { @"C:\Users\Public\", @"C:\Users\tamas\", @"C:\Users\Tara\", @"C:\Users\desktop.ini", @"C:\Users\disk.iso", @"C:\Users\notes.txt" },
            Match(new DirectoryCompletionModel(lister, DirectoryCompletionMode.FilesAndFolders), @"C:\Users\"));
    }

    [TestMethod]
    public void AnExtensionFilterKeepsOnlyThoseFiles_AndEveryFolder()
    {
        var model = new DirectoryCompletionModel(UsersFolder(), DirectoryCompletionMode.FilesAndFolders, fileExtensions: [".ISO"]);

        CollectionAssert.AreEqual(
            new[] { @"C:\Users\Public\", @"C:\Users\tamas\", @"C:\Users\Tara\", @"C:\Users\disk.iso" },
            Match(model, @"C:\Users\"));
    }

    [TestMethod]
    public void ARootIsListed()
    {
        FakeLister lister = new(new Dictionary<string, DirectoryChild[]> { ["/"] = [new("home", true), new("etc", true)] });

        CollectionAssert.AreEqual(new[] { "/etc/", "/home/" }, Match(new DirectoryCompletionModel(lister), "/"));
        CollectionAssert.AreEqual(new[] { "/home/" }, Match(new DirectoryCompletionModel(lister), "/ho"));
    }

    [TestMethod]
    public void WithoutASeparatorNothingIsListed()
    {
        FakeLister lister = UsersFolder();
        var model = new DirectoryCompletionModel(lister);

        Assert.AreEqual(0, Match(model, "C:").Count);
        Assert.AreEqual(0, Match(model, string.Empty).Count);
        Assert.AreEqual(0, lister.Listed.Count);
    }

    [TestMethod]
    public void AFileUrlIsListedFromItsPath_AndKeepsItsPrefix()
    {
        FakeLister lister = new(new Dictionary<string, DirectoryChild[]> { [@"C:/Users/"] = [new("tamas", true)] });
        var model = new DirectoryCompletionModel(lister);

        CollectionAssert.AreEqual(new[] { "file:///C:/Users/tamas/" }, Match(model, "file:///C:/Users/ta"));
    }

    [TestMethod]
    public void AnotherScheme_IsNotListed_OnlyTheHistoryIsOffered()
    {
        FakeLister lister = UsersFolder();
        var history = new Mock<ICompletionModel>();
        history.Setup(h => h.Completions).Returns(["ssh://host/home/me", @"C:\Users\tamas"]);
        var model = new DirectoryCompletionModel(lister, history: history.Object);

        CollectionAssert.AreEqual(new[] { "ssh://host/home/me" }, Match(model, "ssh://host/"));
        Assert.AreEqual(0, lister.Listed.Count);
    }

    [TestMethod]
    public void TheHistoryComesAfterTheLiveMatches_WithoutDuplicates()
    {
        var history = new Mock<ICompletionModel>();
        history.Setup(h => h.Completions).Returns([@"C:\Users\tamas", @"D:\backup\Users\tamas"]);
        var model = new DirectoryCompletionModel(UsersFolder(), history: history.Object);

        // The history's "C:\Users\tamas" and the disk's "C:\Users\tamas\" are one match, the disk's first.
        CollectionAssert.AreEqual(new[] { @"C:\Users\tamas\" }, Match(model, @"C:\Users\tamas"));
        // A history path that only contains the text is offered too, after the prefix matches.
        CollectionAssert.AreEqual(new[] { @"C:\Users\tamas", @"D:\backup\Users\tamas" }, Match(model, @"Users\tamas"));
    }

    [TestMethod]
    public void InsertCompletionAndCompletionsBelongToTheHistory()
    {
        var history = new Mock<ICompletionModel>();
        history.Setup(h => h.Completions).Returns(["C:\\x"]);
        var model = new DirectoryCompletionModel(UsersFolder(), history: history.Object);

        model.InsertCompletion(@"C:\Users");

        history.Verify(h => h.InsertCompletion(@"C:\Users"), Times.Once);
        CollectionAssert.AreEqual(new[] { "C:\\x" }, model.Completions.ToList());
        CollectionAssert.AreEqual(Array.Empty<string>(), new DirectoryCompletionModel(UsersFolder()).Completions.ToList());
    }

    [TestMethod]
    public void NoMoreThanTheCapIsOffered()
    {
        DirectoryChild[] many = [.. Enumerable.Range(0, 500).Select(i => new DirectoryChild($"folder{i:D3}", true))];
        FakeLister lister = new(new Dictionary<string, DirectoryChild[]> { ["/data/"] = many });

        Assert.AreEqual(DirectoryCompletionModel.MaxMatches, Match(new DirectoryCompletionModel(lister), "/data/").Count);
    }

    [TestMethod]
    public void AFolderIsListedOnceWhileTheCacheIsFresh_AndAgainAfterwards()
    {
        long now = 1_000;
        FakeLister lister = UsersFolder();
        var model = new DirectoryCompletionModel(lister, clock: () => now);

        Match(model, @"C:\Users\t");
        Match(model, @"C:\Users\ta");
        Match(model, @"C:\Users\tam");
        Assert.AreEqual(1, lister.Listed.Count, "typing within one folder lists it once");

        now += (long)DirectoryCompletionModel.CacheLifetime.TotalMilliseconds + 1;
        Match(model, @"C:\Users\tam");
        Assert.AreEqual(2, lister.Listed.Count, "an expired listing is read again");

        Match(model, @"C:\Other\x");
        Assert.AreEqual(3, lister.Listed.Count, "another folder is another listing");
    }

    [TestMethod]
    public void AListingThatNeverReturnsCountsAsEmpty()
    {
        using ManualResetEventSlim release = new();
        FakeLister lister = new([])
        {
            Override = _ =>
            {
                release.Wait(TimeSpan.FromSeconds(20));
                return [new DirectoryChild("late", true)];
            },
        };
        var model = new DirectoryCompletionModel(lister);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        List<string> matches = Match(model, @"C:\dead\");
        watch.Stop();
        release.Set();

        Assert.AreEqual(0, matches.Count);
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(10), "the update gave up instead of waiting for the listing");
    }

    [TestMethod]
    public void ASlowListingIsNotCachedAsEmpty_ItFillsTheCacheWhenItEnds_AndIsNotStartedTwice()
    {
        using ManualResetEventSlim release = new();
        FakeLister lister = new([])
        {
            Override = _ =>
            {
                release.Wait(TimeSpan.FromSeconds(20));
                return [new DirectoryChild("late", true)];
            },
        };
        // Zero wait: the listing is still blocked by the gate when the update gives up, so no wall-clock budget is involved.
        var model = new DirectoryCompletionModel(lister, listTimeout: TimeSpan.Zero);

        Assert.AreEqual(0, Match(model, @"C:\slow\").Count, "the first update gives up");
        Assert.AreEqual(0, Match(model, @"C:\slow\l").Count, "and so does the next while it is still running");
        // The listing runs on the thread pool, so it may not have entered the lister yet: wait until it has, and only then
        // count. A second start that is still queued is caught by the count at the end, once everything has finished.
        SpinWait.SpinUntil(() => lister.Listed.Count > 0, TimeSpan.FromSeconds(20));
        Assert.AreEqual(1, lister.Listed.Count, "the running listing was waited on, not started again");

        release.Set();
        for (int i = 0; i < 100 && lister.Listed.Count > 0 && Match(model, @"C:\slow\l").Count == 0; i++)
        {
            Thread.Sleep(50);
        }

        CollectionAssert.AreEqual(new[] { @"C:\slow\late\" }, Match(model, @"C:\slow\l"), "the late listing was kept");
        Assert.AreEqual(1, lister.Listed.Count);
    }

    [TestMethod]
    public void ALateListingIsOfferedWithoutAnotherKeystroke()
    {
        using ManualResetEventSlim release = new();
        FakeLister lister = new([])
        {
            Override = _ =>
            {
                release.Wait(TimeSpan.FromSeconds(20));
                return [new DirectoryChild("late", true)];
            },
        };
        // Zero wait: the listing is still blocked by the gate when the update gives up, so no wall-clock budget is involved.
        var model = new DirectoryCompletionModel(lister, listTimeout: TimeSpan.Zero);

        Assert.AreEqual(0, Match(model, @"C:\slow\").Count, "the update gives up while the listing runs");

        release.Set();
        for (int i = 0; i < 200 && model.MatchingValues.Count == 0; i++)
        {
            Thread.Sleep(20);
        }

        // No second UpdateMatchingValues call: the late listing alone must have published its match.
        CollectionAssert.AreEqual(new[] { @"C:\slow\late\" }, model.MatchingValues.ToList());
    }

    [TestMethod]
    public void ALateListingDoesNotOverwriteANewerUpdate()
    {
        using ManualResetEventSlim release = new();
        FakeLister lister = new([])
        {
            Override = folder =>
            {
                if (folder == @"C:\slow\")
                {
                    release.Wait(TimeSpan.FromSeconds(20));
                    return [new DirectoryChild("late", true)];
                }

                return [new DirectoryChild("other", true)];
            },
        };
        // The gated folder cannot finish, so its update gives up whatever the wait; the newer folder has two seconds to list.
        var model = new DirectoryCompletionModel(lister, listTimeout: TimeSpan.FromSeconds(2));

        Assert.AreEqual(0, Match(model, @"C:\slow\").Count, "the gated listing gives up after the wait");
        CollectionAssert.AreEqual(new[] { @"C:\fast\other\" }, Match(model, @"C:\fast\"), "a newer update publishes first");

        release.Set();
        Thread.Sleep(300);

        CollectionAssert.AreEqual(new[] { @"C:\fast\other\" }, model.MatchingValues.ToList(), "the older late listing does not publish over it");
    }

    [TestMethod]
    public void AListerThatThrowsOffersNothing()
    {
        FakeLister lister = new([]) { Override = _ => throw new InvalidOperationException("boom") };

        Assert.AreEqual(0, Match(new DirectoryCompletionModel(lister), @"C:\x\").Count);
    }

    [TestMethod]
    public async Task AnOlderUpdateThatFinishesLastDoesNotPublishOverTheNewerOne()
    {
        using ManualResetEventSlim olderStarted = new();
        using ManualResetEventSlim releaseOlder = new();
        FakeLister lister = new([])
        {
            Override = folder =>
            {
                if (folder == @"C:\slow\")
                {
                    olderStarted.Set();
                    releaseOlder.Wait(TimeSpan.FromSeconds(20));
                    return [new DirectoryChild("old", true)];
                }

                return [new DirectoryChild("new", true)];
            },
        };
        var model = new DirectoryCompletionModel(lister);

        Task older = Task.Run(() => model.UpdateMatchingValues(@"C:\slow\"));
        Assert.IsTrue(olderStarted.Wait(TimeSpan.FromSeconds(10)));
        model.UpdateMatchingValues(@"C:\fast\");   // started later, finishes first
        CollectionAssert.AreEqual(new[] { @"C:\fast\new\" }, model.MatchingValues.ToList());

        releaseOlder.Set();
        await older;

        CollectionAssert.AreEqual(new[] { @"C:\fast\new\" }, model.MatchingValues.ToList(), "the older update lost");
    }

    [TestMethod]
    public void TheListingRunsOnAWorkerThePendingUpdateCanAbandon()
    {
        FakeLister lister = UsersFolder();
        var model = new DirectoryCompletionModel(lister);

        Match(model, @"C:\Users\ta");

        Assert.AreEqual(1, lister.ThreadIds.Count);
        Assert.AreNotEqual(Environment.CurrentManagedThreadId, lister.ThreadIds[0]);
    }

    [TestMethod]
    public void TheModelIsABlockingCompletionModel() =>
        Assert.IsInstanceOfType<IBlockingCompletionModel>(new DirectoryCompletionModel(UsersFolder()));
}

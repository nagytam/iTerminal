using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Git;
using Moq;

namespace iTerminal.Core.Test.Model.ContentModels;

/// <summary>
/// What every tool-backed drive inherits from <see cref="ContentModelAsyncDrive"/>: the listing runs
/// off the caller's thread, ".." is there at once, a stale listing is dropped, a failure is said in
/// the footer, and the root's ".." leads back to where the panel came from.
/// </summary>
[TestClass]
public class ContentModelAsyncDriveTest
{
    /// <summary>A git drive back-end whose listings are released by the test.</summary>
    private sealed class ScriptedGitDrive : IGitDriveService
    {
        public readonly Dictionary<string, TaskCompletionSource<IReadOnlyList<IEntry>>> Pending = [];

        public TaskCompletionSource<IReadOnlyList<IEntry>> For(string url)
        {
            lock (Pending)
            {
                if (!Pending.TryGetValue(url, out var source))
                {
                    source = new TaskCompletionSource<IReadOnlyList<IEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Pending[url] = source;
                }

                return source;
            }
        }

        public Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken) => For(url.FullUrl).Task;

        public IReadOnlyList<string> Repositories => [];
        public void RememberRepository(string repositoryRoot) { }
        public IEnumerable<Url> ListChildren(Url folderUrl) => [];
        public bool IsReadOnly => true;
        public Task WriteFileAsync(Url url, MemoryStream inputStream) => throw new NotSupportedException();
        public Task<IMemoryFile?> ReadFileAsync(Url url) => throw new NotSupportedException();
        public bool DeleteFile(Url url, bool moveToRecycleBin) => throw new NotSupportedException();
        public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => throw new NotSupportedException();
        public string GetAvailableDiskSpace() => string.Empty;
        public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw new NotSupportedException();
        public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw new NotSupportedException();
        public bool ExistsFile(Url url) => false;
        public bool ExistsFolder(Url url) => false;
        public Task CreateFolderAsync(Url url) => throw new NotSupportedException();
        public Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static IFrequentExecutionGuardService ImmediateGuard()
    {
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());
        return guard.Object;
    }

    private static readonly string Repository = Path.Combine(Path.GetTempPath(), "repo");

    private static (ContentModelGitDrive Model, ScriptedGitDrive Drive, SettingsService Settings) Create()
    {
        var settings = new SettingsService();
        var drive = new ScriptedGitDrive();
        var model = new ContentModelGitDrive("LeftPanel", settings, drive, folderHistoryService: null, ImmediateGuard());
        return (model, drive, settings);
    }

    private static VirtualDriveEntry File(Url folder, string name) =>
        new(new Url(folder.FullUrl + name), name, "1 B");

    [Timeout(60_000)]
    [TestMethod]
    public void NavigatingReturnsAtOnceWithTheWayUpAlreadyListed()
    {
        var (model, _, _) = Create();
        var url = GitDriveUrl.Build(Repository, "HEAD");

        model.SetUrl(url);

        Assert.AreEqual("..", model.Entries.Single().DisplayName, "the listing has not landed, the way up has");
        Assert.IsTrue(model.IsLoading);
        StringAssert.Contains(model.GetHeader(200), "(reading…)");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheListingReplacesTheWayUpRowWhenItLands()
    {
        var (model, drive, _) = Create();
        var url = GitDriveUrl.Build(Repository, "HEAD");
        model.SetUrl(url);

        drive.For(url.FullUrl).SetResult([File(url, "a.txt"), File(url, "b.txt")]);
        await model.ListingTask;

        CollectionAssert.AreEqual(new[] { "..", "a.txt", "b.txt" }, model.Entries.AsSnapshot().Select(e => e.DisplayName).ToArray());
        Assert.IsFalse(model.IsLoading);
        Assert.IsTrue(model.ExistsFile(new Url(url.FullUrl + "a.txt")), "existence is answered from the listing");
        Assert.IsFalse(model.ExistsFile(new Url(url.FullUrl + "c.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AListingForAFolderAlreadyLeftIsDropped()
    {
        var (model, drive, _) = Create();
        var first = GitDriveUrl.Build(Repository, "HEAD", "old");
        var second = GitDriveUrl.Build(Repository, "HEAD", "new");
        model.SetUrl(first);
        var staleTask = model.ListingTask;
        model.SetUrl(second);

        drive.For(second.FullUrl).SetResult([File(second, "new.txt")]);
        await model.ListingTask;
        drive.For(first.FullUrl).SetResult([File(first, "old.txt")]);
        await staleTask;

        CollectionAssert.AreEqual(new[] { "..", "new.txt" }, model.Entries.AsSnapshot().Select(e => e.DisplayName).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedListingIsSaidInTheFooterAndKeepsTheWayUp()
    {
        var (model, drive, _) = Create();
        var url = GitDriveUrl.Build(Repository, "nope");
        model.SetUrl(url);

        drive.For(url.FullUrl).SetException(new InvalidOperationException("fatal: Not a valid object name nope"));
        await model.ListingTask;

        Assert.AreEqual("..", model.Entries.Single().DisplayName);
        StringAssert.Contains(model.GetFooter(), "fatal: Not a valid object name nope");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheRootsWayUpIsTheFolderThePanelCameFrom()
    {
        var (model, _, settings) = Create();
        var home = new Url(Path.GetTempPath());
        ContentModelAsyncDrive.RememberReturnUrl(settings, "LeftPanel", home, new Url(Container.GIT));

        model.SetUrl(new Url(Container.GIT));

        Assert.AreEqual(home.FullUrl, model.Entries.Single().Url.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BelowTheRootTheWayUpStaysOnTheDrive()
    {
        var (model, _, _) = Create();

        model.SetUrl(GitDriveUrl.Build(Repository, "HEAD", "src"));

        Assert.AreEqual(GitDriveUrl.Build(Repository, "HEAD").FullUrl, model.Entries.Single().Url.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnlyACrossingFromOutsideIsRemembered()
    {
        var settings = new SettingsService();
        var disk = new Url(Path.GetTempPath());

        ContentModelAsyncDrive.RememberReturnUrl(settings, "P", disk, new Url(Container.DOCKER));
        ContentModelAsyncDrive.RememberReturnUrl(settings, "P", new Url("docker://web/"), new Url(Container.SEARCH_RESULTS));
        ContentModelAsyncDrive.RememberReturnUrl(settings, "P", disk, new Url(Container.MUSIC_CATALOG));

        Assert.AreEqual(disk.FullUrl, ContentModelAsyncDrive.GetReturnUrl(settings, "P").FullUrl,
            "docker → search keeps the disk folder; a music drive is not one of these drives");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheHeaderNamesTheRepositoryAndRevisionInWords()
    {
        var (model, _, _) = Create();

        model.SetUrl(GitDriveUrl.Build(Repository, new string('a', 40), "src"));

        Assert.AreEqual("git: repo @ aaaaaaaa:/src (reading…)", model.GetHeader(200));
    }
}

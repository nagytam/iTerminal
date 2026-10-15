using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Git;
using iTerminal.Core.Services.Processes;
using iTerminal.Core.Test.Helper;
using System.Text;

namespace iTerminal.Core.Test.Services.Git;

/// <summary>
/// The git:// drive against a real repository: two commits, a branch whose name has a slash, a
/// file whose name has a space, and a file that changed between the commits — so reading the old
/// revision is distinguishable from reading the working tree.
/// </summary>
[TestClass]
public class GitDriveServiceTest
{
    private static string? _root;
    private static string _firstCommit = string.Empty;

    [ClassInitialize]
    public static void CreateRepository(TestContext _)
    {
        if (!GitAvailable())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "gitdrive_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src", "lib"));
        Run(root, "init", "-b", "main");
        Run(root, "config", "user.email", "test@example.com");
        Run(root, "config", "user.name", "Test");
        Run(root, "config", "core.autocrlf", "false");

        File.WriteAllText(Path.Combine(root, "readme.md"), "version one\n");
        File.WriteAllText(Path.Combine(root, "src", "lib", "my notes.txt"), "deep file\n");
        Run(root, "add", "-A");
        Run(root, "commit", "-m", "first commit");
        _firstCommit = RunOutput(root, "rev-parse", "HEAD").Trim();

        File.WriteAllText(Path.Combine(root, "readme.md"), "version two\n");
        Run(root, "commit", "-am", "second commit");
        Run(root, "branch", "feature/login");
        Run(root, "tag", "v1.0", _firstCommit);
        _root = root;
    }

    [ClassCleanup]
    public static void DeleteRepository()
    {
        if (_root is null)
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    private static string Root => _root ?? throw new AssertInconclusiveException("git not available");

    private static GitDriveService CreateService(ISettingsService? settings = null) =>
        new(new ExternalProcessRunner(), settings ?? new SettingsService());

    [TestMethod]
    public async Task ARepositoryListsHeadItsBranchesTagsAndCommits()
    {
        var service = CreateService();

        var entries = await service.ListAsync(GitDriveUrl.Build(Root), CancellationToken.None);
        var names = entries.Select(e => e.DisplayName).ToList();

        Assert.AreEqual("HEAD", names[0], "what is checked out comes first");
        CollectionAssert.Contains(names, "main");
        CollectionAssert.Contains(names, "feature/login");
        CollectionAssert.Contains(names, "v1.0");
        Assert.IsTrue(names.Any(n => n.EndsWith("first commit", StringComparison.Ordinal)), string.Join(", ", names));
        Assert.IsTrue(entries.All(e => e.Url.PathType == PathType.Folder), "every revision is a folder");
        Assert.AreEqual("Tag", entries.Single(e => e.DisplayName == "v1.0").FileSize);
    }

    [TestMethod]
    public async Task ABranchWithASlashIsOneFolderNotTwo()
    {
        var service = CreateService();
        var branch = (await service.ListAsync(GitDriveUrl.Build(Root), CancellationToken.None))
            .Single(e => e.DisplayName == "feature/login");

        GitDriveUrl.Parse(branch.Url, out var repository, out var revision, out var path);

        Assert.AreEqual(Root, repository);
        Assert.AreEqual("feature/login", revision);
        Assert.AreEqual(string.Empty, path);

        var tree = await service.ListAsync(branch.Url, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "src", "readme.md" }, tree.Select(e => e.DisplayName).ToArray(), "folders first");
    }

    [TestMethod]
    public async Task ATreeListsFoldersThenFilesWithSizes()
    {
        var service = CreateService();

        var lib = await service.ListAsync(GitDriveUrl.Build(Root, "HEAD", "src/lib"), CancellationToken.None);

        var file = lib.Single();
        Assert.AreEqual("my notes.txt", file.DisplayName);
        Assert.AreEqual("my notes.txt", file.Url.Name, "the copy target takes the real name, unescaped");
        Assert.AreEqual(PathType.File, file.Url.PathType);
        Assert.AreEqual(10, ((VirtualDriveEntry)file).Size);
    }

    [TestMethod]
    public async Task AnOldRevisionReadsTheContentItHadThen()
    {
        var service = CreateService();
        var oldReadme = (await service.ListAsync(GitDriveUrl.Build(Root, _firstCommit), CancellationToken.None))
            .Single(e => e.DisplayName == "readme.md");

        var file = await service.ReadFileAsync(oldReadme.Url);

        Assert.AreEqual("version one\n", Encoding.UTF8.GetString(file!.Content!));
        Assert.AreEqual("version two\n", File.ReadAllText(Path.Combine(Root, "readme.md")), "the working tree is untouched");
    }

    [TestMethod]
    public async Task StreamingReportsTheBytesCopied()
    {
        var service = CreateService();
        long reported = 0;
        using var destination = new MemoryStream();

        await service.StreamToAsync(GitDriveUrl.Build(Root, "HEAD", "src/lib/my notes.txt", isFolder: false), destination,
            new SynchronousProgressForTest(value => reported = value), CancellationToken.None);

        Assert.AreEqual("deep file\n", Encoding.UTF8.GetString(destination.ToArray()));
        Assert.AreEqual(10, reported);
    }

    [TestMethod]
    public async Task OpeningARepositoryRemembersItAtTheDriveRoot()
    {
        var settings = new SettingsService();
        var service = CreateService(settings);

        await service.ListAsync(GitDriveUrl.Build(Root), CancellationToken.None);
        var root = await service.ListAsync(new Url(Container.GIT), CancellationToken.None);

        var repository = root.Single();
        Assert.AreEqual(Path.GetFileName(Root), repository.DisplayName);
        Assert.AreEqual(GitDriveUrl.Build(Root).FullUrl, repository.Url.FullUrl);
    }

    [TestMethod]
    public async Task ListChildrenFeedsAFolderCopy()
    {
        var service = CreateService();

        var children = service.ListChildren(GitDriveUrl.Build(Root, "HEAD", "src")).ToList();

        Assert.AreEqual(GitDriveUrl.Build(Root, "HEAD", "src/lib").FullUrl, children.Single().FullUrl);
        await Task.CompletedTask;
    }

    [TestMethod]
    public void TheHistoryIsReadOnly()
    {
        var service = CreateService();

        Assert.IsTrue(service.IsReadOnly);
        Assert.ThrowsExactly<NotSupportedException>(() => service.DeleteFile(GitDriveUrl.Build(Root, "HEAD", "readme.md", isFolder: false), false));
    }

    [TestMethod]
    public async Task AnUnknownRevisionFailsWithGitsOwnMessage()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.ListAsync(GitDriveUrl.Build(Root, "no-such-branch"), CancellationToken.None));

        StringAssert.Contains(exception.Message, "no-such-branch");
    }

    [TestMethod]
    public void ParseTreeReadsNamesVerbatim()
    {
        var output = "100644 blob 3b18e512dba79e4c8300dd08aeb37f8e728b8dad      12\tmy notes.txt\0"
                   + "040000 tree 4b825dc642cb6eb9a060e54bf8d69288fbee4904       -\tsrc\0"
                   + "160000 commit 0123456789abcdef0123456789abcdef01234567       -\tvendor\0";

        var items = GitDriveService.ParseTree(output).ToList();

        Assert.AreEqual(3, items.Count);
        Assert.AreEqual(("blob", "my notes.txt", 12L), (items[0].Type, items[0].Name, items[0].Size));
        Assert.AreEqual(("tree", "src", 0L), (items[1].Type, items[1].Name, items[1].Size));
        Assert.AreEqual("commit", items[2].Type);
    }

    [TestMethod]
    public void UrlsRoundTripAWindowsPathAndASlashedBranch()
    {
        var url = GitDriveUrl.Build(@"C:\src\my app", "feature/x", "docs/guide", isFolder: true);

        GitDriveUrl.Parse(url, out var repository, out var revision, out var path);

        Assert.AreEqual(ContainerType.Git, url.ContainerType);
        Assert.AreEqual(@"C:\src\my app", repository);
        Assert.AreEqual("feature/x", revision);
        Assert.AreEqual("docs/guide", path);
        Assert.AreEqual("guide", url.Name);
    }

    [TestMethod]
    public async Task ARepositoryThatCannotBeReadSaysWhy()
    {
        var runner = new FakeProcessRunner().Answer("git -C", string.Empty, exitCode: 128, stderr: "fatal: not a git repository");
        var service = new GitDriveService(runner);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.ListAsync(GitDriveUrl.Build(@"C:\nowhere"), CancellationToken.None));

        Assert.AreEqual("fatal: not a git repository", exception.Message);
    }

    private sealed class SynchronousProgressForTest(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    #region git

    private static bool GitAvailable()
    {
        try
        {
            return RunOutput(Path.GetTempPath(), "--version").StartsWith("git version", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static void Run(string root, params string[] arguments) => RunOutput(root, arguments);

    private static string RunOutput(string root, params string[] arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"git {string.Join(' ', arguments)} did not finish.");
        }

        Assert.AreEqual(0, process.ExitCode, $"git {string.Join(' ', arguments)} failed: {error.Result}");
        return output.Result;
    }

    #endregion
}

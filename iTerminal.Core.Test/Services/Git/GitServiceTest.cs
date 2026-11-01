using iTerminal.Core.Services.Git;

namespace iTerminal.Core.Test.Services.Git;

[TestClass]
public class GitServiceTest
{
    private static bool GitAvailable()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            process!.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string CreateTempRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), "gitsvc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Run(root, "init");
        Run(root, "config user.email test@example.com");
        Run(root, "config user.name Test");
        Run(root, "commit --allow-empty -m initial");
        return root;
    }

    /// <summary>
    /// Runs a setup command to completion and fails the test if it did not succeed. It used to give
    /// git ten seconds and carry on regardless: with the whole solution's tests running side by side a
    /// commit could still be going — holding index.lock — when the service under test ran its own
    /// command, and the test failed on something it was not testing.
    /// </summary>
    private static void Run(string root, string arguments)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        // Drained while waiting: a full pipe would stall git on its own output.
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"git {arguments} did not finish in two minutes.");
        }
        Assert.AreEqual(0, process.ExitCode, $"git {arguments} failed:{Environment.NewLine}{output.Result}{error.Result}");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GetRepositoryRootAsync_OutsideRepo_ReturnsNull()
    {
        if (!GitAvailable())
        {
            Assert.Inconclusive("git not available");
            return;
        }

        var temp = Path.Combine(Path.GetTempPath(), "notrepo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var service = new GitService();
            var root = await service.GetRepositoryRootAsync(temp);
            Assert.IsNull(root);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GetStatusAsync_ClassifiesUntrackedStagedAndModified()
    {
        if (!GitAvailable())
        {
            Assert.Inconclusive("git not available");
            return;
        }

        var root = CreateTempRepo();
        try
        {
            // Tracked + committed file we will modify.
            File.WriteAllText(Path.Combine(root, "tracked.txt"), "v1\n");
            Run(root, "add tracked.txt");
            Run(root, "commit -m add-tracked");
            File.WriteAllText(Path.Combine(root, "tracked.txt"), "v2\n");

            // A staged new file.
            File.WriteAllText(Path.Combine(root, "staged.txt"), "s\n");
            Run(root, "add staged.txt");

            // An untracked file.
            File.WriteAllText(Path.Combine(root, "untracked.txt"), "u\n");

            var service = new GitService();
            var resolvedRoot = await service.GetRepositoryRootAsync(root);
            Assert.IsNotNull(resolvedRoot);

            var status = await service.GetStatusAsync(resolvedRoot!);

            Assert.IsTrue(status.Unstaged.Any(f => f.Path.Contains("tracked.txt") && !f.IsUntracked),
                "modified tracked file should be unstaged");
            Assert.IsTrue(status.Unstaged.Any(f => f.Path.Contains("untracked.txt") && f.IsUntracked),
                "untracked file should be unstaged + untracked");
            Assert.IsTrue(status.Staged.Any(f => f.Path.Contains("staged.txt")),
                "added file should be staged");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task StageCommitFlow_MovesFileToStagedThenClears()
    {
        if (!GitAvailable())
        {
            Assert.Inconclusive("git not available");
            return;
        }

        var root = CreateTempRepo();
        try
        {
            File.WriteAllText(Path.Combine(root, "new.txt"), "hello\n");

            var service = new GitService();
            var resolvedRoot = (await service.GetRepositoryRootAsync(root))!;

            var stage = await service.StageAsync(resolvedRoot, "new.txt");
            Assert.IsTrue(stage.Success, stage.Output);

            var staged = await service.GetStatusAsync(resolvedRoot);
            Assert.IsTrue(staged.Staged.Any(f => f.Path.Contains("new.txt")));

            var commit = await service.CommitAsync(resolvedRoot, "add new file", amend: false);
            Assert.IsTrue(commit.Success, commit.Output);

            var afterCommit = await service.GetStatusAsync(resolvedRoot);
            Assert.IsFalse(afterCommit.Staged.Any(f => f.Path.Contains("new.txt")));

            var log = await service.GetLogAsync(resolvedRoot, 10);
            Assert.IsTrue(log.Any(c => c.Subject == "add new file"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CreateAndCheckoutBranch_SwitchesCurrentBranch()
    {
        if (!GitAvailable())
        {
            Assert.Inconclusive("git not available");
            return;
        }

        var root = CreateTempRepo();
        try
        {
            var service = new GitService();
            var resolvedRoot = (await service.GetRepositoryRootAsync(root))!;
            var initial = (await service.GetBranchesAsync(resolvedRoot)).Single(b => b.IsCurrent).Name;

            var create = await service.CreateBranchAsync(resolvedRoot, "feature/x");
            Assert.IsTrue(create.Success, create.Output);

            var branches = await service.GetBranchesAsync(resolvedRoot);
            Assert.IsTrue(branches.Any(b => b.Name == "feature/x" && b.IsCurrent),
                "new branch should exist and be current");

            var checkout = await service.CheckoutBranchAsync(resolvedRoot, initial);
            Assert.IsTrue(checkout.Success, checkout.Output);

            var status = await service.GetStatusAsync(resolvedRoot);
            Assert.AreEqual(initial, status.Branch);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PushAndFetch_AgainstLocalBareRemote_Succeeds()
    {
        if (!GitAvailable())
        {
            Assert.Inconclusive("git not available");
            return;
        }

        var root = CreateTempRepo();
        var bare = Path.Combine(Path.GetTempPath(), "barerepo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bare);
        Run(bare, "init --bare");

        try
        {
            var service = new GitService();
            var resolvedRoot = (await service.GetRepositoryRootAsync(root))!;
            var current = (await service.GetBranchesAsync(resolvedRoot)).Single(b => b.IsCurrent).Name;

            Run(root, $"remote add origin \"{bare}\"");
            Run(root, $"push -u origin {current}");

            // A new local commit puts the branch ahead of origin.
            File.WriteAllText(Path.Combine(root, "remote.txt"), "r\n");
            Run(root, "add remote.txt");
            Run(root, "commit -m remote-test");

            var push = await service.PushAsync(resolvedRoot);
            Assert.IsTrue(push.Success, push.Output);

            var fetch = await service.FetchAsync(resolvedRoot);
            Assert.IsTrue(fetch.Success, fetch.Output);

            var status = await service.GetStatusAsync(resolvedRoot);
            Assert.AreEqual(0, status.Ahead, "after push the branch should not be ahead of origin");
        }
        finally
        {
            TryDelete(root);
            TryDelete(bare);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task StashFlow_PushHidesChanges_PopRestoresThem()
    {
        if (!GitAvailable())
        {
            Assert.Inconclusive("git not available");
            return;
        }

        var root = CreateTempRepo();
        try
        {
            var tracked = Path.Combine(root, "tracked.txt");
            File.WriteAllText(tracked, "v1\n");
            Run(root, "add tracked.txt");
            Run(root, "commit -m add-tracked");
            File.WriteAllText(tracked, "v2\n");

            var service = new GitService();
            var resolvedRoot = (await service.GetRepositoryRootAsync(root))!;

            var stash = await service.StashPushAsync(resolvedRoot);
            Assert.IsTrue(stash.Success, stash.Output);

            // Working tree is clean and the change is hidden in a stash.
            var afterStash = await service.GetStatusAsync(resolvedRoot);
            Assert.IsFalse(afterStash.Unstaged.Any(f => f.Path.Contains("tracked.txt")));
            var stashes = await service.GetStashesAsync(resolvedRoot);
            Assert.AreEqual(1, stashes.Count);

            var pop = await service.StashPopAsync(resolvedRoot, 0);
            Assert.IsTrue(pop.Success, pop.Output);

            // The change is back and the stash is gone (ignore CRLF normalization on Windows).
            Assert.AreEqual("v2", File.ReadAllText(tracked).Trim());
            Assert.AreEqual(0, (await service.GetStashesAsync(resolvedRoot)).Count);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GetCommitDiffAsync_ContainsSubjectAndChangedFile()
    {
        if (!GitAvailable())
        {
            Assert.Inconclusive("git not available");
            return;
        }

        var root = CreateTempRepo();
        try
        {
            File.WriteAllText(Path.Combine(root, "feature.txt"), "hello\n");
            Run(root, "add feature.txt");
            Run(root, "commit -m add-feature-file");

            var service = new GitService();
            var resolvedRoot = (await service.GetRepositoryRootAsync(root))!;
            var head = (await service.GetLogAsync(resolvedRoot, 1)).Single();

            var diff = await service.GetCommitDiffAsync(resolvedRoot, head.ShortHash);

            StringAssert.Contains(diff, "add-feature-file");
            StringAssert.Contains(diff, "feature.txt");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MergeAndDeleteBranch_IntegratesAndRemovesBranch()
    {
        if (!GitAvailable())
        {
            Assert.Inconclusive("git not available");
            return;
        }

        var root = CreateTempRepo();
        try
        {
            var service = new GitService();
            var resolvedRoot = (await service.GetRepositoryRootAsync(root))!;
            var initial = (await service.GetBranchesAsync(resolvedRoot)).Single(b => b.IsCurrent).Name;

            // Commit a file on a feature branch.
            await service.CreateBranchAsync(resolvedRoot, "feature");
            File.WriteAllText(Path.Combine(root, "feature.txt"), "f\n");
            Run(root, "add feature.txt");
            Run(root, "commit -m feature-commit");

            // Fast-forward merge it back into the initial branch.
            await service.CheckoutBranchAsync(resolvedRoot, initial);
            var merge = await service.MergeBranchAsync(resolvedRoot, "feature");
            Assert.IsTrue(merge.Success, merge.Output);
            Assert.IsFalse(await service.IsMergeInProgressAsync(resolvedRoot));
            Assert.IsTrue((await service.GetLogAsync(resolvedRoot, 10)).Any(c => c.Subject == "feature-commit"));

            // Now the branch is merged and can be deleted.
            var delete = await service.DeleteBranchAsync(resolvedRoot, "feature", force: false);
            Assert.IsTrue(delete.Success, delete.Output);
            Assert.IsFalse((await service.GetBranchesAsync(resolvedRoot)).Any(b => b.Name == "feature"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MergeConflict_ThenAbort_RestoresCleanState()
    {
        if (!GitAvailable())
        {
            Assert.Inconclusive("git not available");
            return;
        }

        var root = CreateTempRepo();
        try
        {
            var file = Path.Combine(root, "conflict.txt");
            File.WriteAllText(file, "base\n");
            Run(root, "add conflict.txt");
            Run(root, "commit -m base");

            var service = new GitService();
            var resolvedRoot = (await service.GetRepositoryRootAsync(root))!;
            var initial = (await service.GetBranchesAsync(resolvedRoot)).Single(b => b.IsCurrent).Name;

            // Divergent change on a branch.
            await service.CreateBranchAsync(resolvedRoot, "other");
            File.WriteAllText(file, "other\n");
            Run(root, "add conflict.txt");
            Run(root, "commit -m other");

            // Conflicting change on the initial branch.
            await service.CheckoutBranchAsync(resolvedRoot, initial);
            File.WriteAllText(file, "main\n");
            Run(root, "add conflict.txt");
            Run(root, "commit -m main");

            // The merge conflicts and leaves a merge in progress.
            var merge = await service.MergeBranchAsync(resolvedRoot, "other");
            Assert.IsFalse(merge.Success, "conflicting merge should not succeed");
            Assert.IsTrue(await service.IsMergeInProgressAsync(resolvedRoot));

            // Aborting restores the clean pre-merge state.
            var abort = await service.AbortMergeAsync(resolvedRoot);
            Assert.IsTrue(abort.Success, abort.Output);
            Assert.IsFalse(await service.IsMergeInProgressAsync(resolvedRoot));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task StageAllAndUnstageAll_MoveEveryChangeAtOnce()
    {
        if (!GitAvailable())
        {
            Assert.Inconclusive("git not available");
            return;
        }

        var root = CreateTempRepo();
        try
        {
            File.WriteAllText(Path.Combine(root, "tracked.txt"), "v1\n");
            Run(root, "add tracked.txt");
            Run(root, "commit -m add-tracked");

            File.WriteAllText(Path.Combine(root, "tracked.txt"), "v2\n");
            File.WriteAllText(Path.Combine(root, "untracked.txt"), "new\n");

            var service = new GitService();
            var resolvedRoot = (await service.GetRepositoryRootAsync(root))!;

            var stageAll = await service.StageAllAsync(resolvedRoot);
            Assert.IsTrue(stageAll.Success, stageAll.Output);

            var staged = await service.GetStatusAsync(resolvedRoot);
            Assert.AreEqual(2, staged.Staged.Count, "stage-all covers untracked files too");
            Assert.AreEqual(0, staged.Unstaged.Count);

            var unstageAll = await service.UnstageAllAsync(resolvedRoot);
            Assert.IsTrue(unstageAll.Success, unstageAll.Output);

            var unstaged = await service.GetStatusAsync(resolvedRoot);
            Assert.AreEqual(0, unstaged.Staged.Count);
            Assert.AreEqual(2, unstaged.Unstaged.Count, "unstaging must not touch the working tree");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GetLastCommitMessageAsync_ReturnsTheWholeMessage()
    {
        if (!GitAvailable())
        {
            Assert.Inconclusive("git not available");
            return;
        }

        var root = CreateTempRepo();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "a\n");
            Run(root, "add a.txt");

            var service = new GitService();
            var resolvedRoot = (await service.GetRepositoryRootAsync(root))!;
            var commit = await service.CommitAsync(resolvedRoot, "Subject line", amend: false);
            Assert.IsTrue(commit.Success, commit.Output);

            Assert.AreEqual("Subject line", await service.GetLastCommitMessageAsync(resolvedRoot));

            // Amending is what the message is read back for; the new one must replace it outright.
            var amend = await service.CommitAsync(resolvedRoot, "Subject line, corrected", amend: true);
            Assert.IsTrue(amend.Success, amend.Output);
            Assert.AreEqual("Subject line, corrected", await service.GetLastCommitMessageAsync(resolvedRoot));
            Assert.AreEqual(1, (await service.GetLogAsync(resolvedRoot, 10)).Count(c => c.Subject.StartsWith("Subject line")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GetBranchesAsync_SeparatesRemoteTrackingBranchesAndCheckingOneOutTracksIt()
    {
        if (!GitAvailable())
        {
            Assert.Inconclusive("git not available");
            return;
        }

        var origin = Path.Combine(Path.GetTempPath(), "gitsvc_origin_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(origin);
        Run(origin, "init --bare");

        var root = CreateTempRepo();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "a\n");
            Run(root, "add a.txt");
            Run(root, "commit -m first");
            Run(root, $"remote add origin \"{origin}\"");

            var service = new GitService();
            var resolvedRoot = (await service.GetRepositoryRootAsync(root))!;
            Assert.AreEqual("origin", await service.GetDefaultRemoteAsync(resolvedRoot));

            // Publish a branch, then delete it locally so only the remote-tracking ref is left.
            var initial = (await service.GetBranchesAsync(resolvedRoot)).Single(b => b.IsCurrent).Name;
            await service.CreateBranchAsync(resolvedRoot, "feature");
            var publish = await service.PushAsync(resolvedRoot, setUpstream: true);
            Assert.IsTrue(publish.Success, publish.Output);
            await service.CheckoutBranchAsync(resolvedRoot, initial);
            await service.DeleteBranchAsync(resolvedRoot, "feature", force: true);

            var withoutRemotes = await service.GetBranchesAsync(resolvedRoot);
            Assert.IsFalse(withoutRemotes.Any(b => b.IsRemote), "refs/heads holds no remote-tracking branches");

            var withRemotes = await service.GetBranchesAsync(resolvedRoot, includeRemotes: true);
            var remote = withRemotes.SingleOrDefault(b => b.Name == "origin/feature");
            Assert.IsNotNull(remote, "the published branch should be listed as a remote-tracking one");
            Assert.IsTrue(remote!.IsRemote);
            Assert.IsFalse(withRemotes.Any(b => b.Name.EndsWith("/HEAD", StringComparison.Ordinal)),
                "origin/HEAD is a symbolic ref, not a branch anyone can check out");

            // Checking out the remote row is what recreates the local branch, tracking it.
            var checkout = await service.CheckoutRemoteBranchAsync(resolvedRoot, "origin/feature");
            Assert.IsTrue(checkout.Success, checkout.Output);

            var afterCheckout = await service.GetBranchesAsync(resolvedRoot);
            var local = afterCheckout.SingleOrDefault(b => b.Name == "feature");
            Assert.IsNotNull(local);
            Assert.IsTrue(local!.IsCurrent);
            Assert.AreEqual("origin/feature", local.Upstream);
        }
        finally
        {
            TryDelete(root);
            TryDelete(origin);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task StashPushAsync_KeepsTheMessageItWasGiven()
    {
        if (!GitAvailable())
        {
            Assert.Inconclusive("git not available");
            return;
        }

        var root = CreateTempRepo();
        try
        {
            var tracked = Path.Combine(root, "tracked.txt");
            File.WriteAllText(tracked, "v1\n");
            Run(root, "add tracked.txt");
            Run(root, "commit -m add-tracked");
            File.WriteAllText(tracked, "v2\n");

            var service = new GitService();
            var resolvedRoot = (await service.GetRepositoryRootAsync(root))!;

            var stash = await service.StashPushAsync(resolvedRoot, "half-done refactor");
            Assert.IsTrue(stash.Success, stash.Output);

            var stashes = await service.GetStashesAsync(resolvedRoot);
            Assert.AreEqual(1, stashes.Count);
            StringAssert.Contains(stashes[0].Message, "half-done refactor");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void TryDelete(string root)
    {
        try
        {
            // Clear read-only attributes git sets on objects so the tree can be removed.
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(root, true);
        }
        catch
        {
        }
    }
}

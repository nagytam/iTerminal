using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs.Git;
using iTerminal.Core.Model.Git;
using iTerminal.Core.Services.Git;
using Moq;

namespace iTerminal.Core.Test.Dialogs.Git;

/// <summary>
/// The Ctrl+G git client. These tests drive it the way a user does — keys in, screen out — because
/// what is worth pinning down about it is behavioural: that the key a panel advertises does what it
/// says, that a panel with nothing in it does not hold rows the panels with content need, and that a
/// command's failure is legible rather than truncated into the footer.
/// </summary>
[TestClass]
public class GitClientDialogTest
{
    /// <summary>
    /// <see cref="IGitService"/> fake that records what it was asked to do and hands back whatever
    /// the test set up. Every call is logged as a single string so an assertion can name the command
    /// it expects without reaching into the dialog's private state.
    /// </summary>
    private sealed class RecordingGitService : IGitService
    {
        public List<string> Calls { get; } = [];
        public string? Root { get; set; } = "/repo";
        public GitRepositoryStatus Status { get; set; } = GitRepositoryStatus.Empty;
        public IReadOnlyList<GitCommit> Commits { get; set; } = [];
        public IReadOnlyList<GitBranch> Branches { get; set; } = [];
        public IReadOnlyList<GitStash> Stashes { get; set; } = [];
        public string LastCommitMessage { get; set; } = string.Empty;

        /// <summary>Failure to hand back from the next mutating call, keyed by the call it should fail.</summary>
        public Dictionary<string, string> Failures { get; } = [];

        private Task<GitResult> Record(string call)
        {
            Calls.Add(call);
            return Task.FromResult(Failures.TryGetValue(call, out var output) ? GitResult.Fail(output) : GitResult.Ok());
        }

        public Task<string?> GetRepositoryRootAsync(string workingDirectory, CancellationToken cancellationToken = default)
            => Task.FromResult(Root);

        public Task<GitRepositoryStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default)
            => Task.FromResult(Status);

        public Task<IReadOnlyList<GitCommit>> GetLogAsync(string repositoryRoot, int maxCount, CancellationToken cancellationToken = default)
            => Task.FromResult(Commits);

        public Task<string> GetDiffAsync(string repositoryRoot, string relativePath, bool staged, bool isUntracked, CancellationToken cancellationToken = default)
            => Task.FromResult($"--- a/{relativePath}\n+++ b/{relativePath}\n+changed");

        public Task<string> GetCommitDiffAsync(string repositoryRoot, string commit, CancellationToken cancellationToken = default)
            => Task.FromResult($"commit {commit}");

        public Task<GitResult> StageAsync(string repositoryRoot, string relativePath, CancellationToken cancellationToken = default)
            => Record($"stage:{relativePath}");

        public Task<GitResult> StageAllAsync(string repositoryRoot, CancellationToken cancellationToken = default)
            => Record("stage-all");

        public Task<GitResult> UnstageAsync(string repositoryRoot, string relativePath, CancellationToken cancellationToken = default)
            => Record($"unstage:{relativePath}");

        public Task<GitResult> UnstageAllAsync(string repositoryRoot, CancellationToken cancellationToken = default)
            => Record("unstage-all");

        public Task<GitResult> DiscardAsync(string repositoryRoot, string relativePath, bool isUntracked, CancellationToken cancellationToken = default)
            => Record($"discard:{relativePath}");

        public Task<GitResult> CommitAsync(string repositoryRoot, string message, bool amend, CancellationToken cancellationToken = default)
            => Record($"commit:{(amend ? "amend:" : string.Empty)}{message}");

        public Task<string> GetLastCommitMessageAsync(string repositoryRoot, CancellationToken cancellationToken = default)
            => Task.FromResult(LastCommitMessage);

        public Task<IReadOnlyList<GitBranch>> GetBranchesAsync(string repositoryRoot, bool includeRemotes = false, CancellationToken cancellationToken = default)
            => Task.FromResult(Branches);

        public Task<GitResult> CheckoutBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default)
            => Record($"checkout:{branchName}");

        public Task<GitResult> CheckoutRemoteBranchAsync(string repositoryRoot, string remoteBranchName, CancellationToken cancellationToken = default)
            => Record($"checkout-remote:{remoteBranchName}");

        public Task<GitResult> CreateBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default)
            => Record($"create-branch:{branchName}");

        public Task<GitResult> MergeBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default)
            => Record($"merge:{branchName}");

        public Task<GitResult> DeleteBranchAsync(string repositoryRoot, string branchName, bool force, CancellationToken cancellationToken = default)
            => Record($"delete-branch:{branchName}");

        public Task<GitResult> AbortMergeAsync(string repositoryRoot, CancellationToken cancellationToken = default)
            => Record("abort-merge");

        public bool MergeInProgress { get; set; }

        public Task<bool> IsMergeInProgressAsync(string repositoryRoot, CancellationToken cancellationToken = default)
            => Task.FromResult(MergeInProgress);

        public Task<GitResult> FetchAsync(string repositoryRoot, CancellationToken cancellationToken = default)
            => Record("fetch");

        public Task<GitResult> PullAsync(string repositoryRoot, CancellationToken cancellationToken = default)
            => Record("pull");

        public Task<GitResult> PullRebaseAsync(string repositoryRoot, CancellationToken cancellationToken = default)
            => Record("pull:rebase");

        public Task<GitResult> PushAsync(string repositoryRoot, bool setUpstream = false, CancellationToken cancellationToken = default)
            => Record($"push:set-upstream={setUpstream}");

        public Task<string?> GetDefaultRemoteAsync(string repositoryRoot, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("origin");

        public Task<IReadOnlyList<GitStash>> GetStashesAsync(string repositoryRoot, CancellationToken cancellationToken = default)
            => Task.FromResult(Stashes);

        public Task<GitResult> StashPushAsync(string repositoryRoot, string? message = null, CancellationToken cancellationToken = default)
            => Record($"stash-push:{message}");

        public Task<GitResult> StashPopAsync(string repositoryRoot, int index, CancellationToken cancellationToken = default)
            => Record($"stash-pop:{index}");

        public Task<GitResult> StashApplyAsync(string repositoryRoot, int index, CancellationToken cancellationToken = default)
            => Record($"stash-apply:{index}");

        public Task<GitResult> StashDropAsync(string repositoryRoot, int index, CancellationToken cancellationToken = default)
            => Record($"stash-drop:{index}");
    }

    private static readonly Size ScreenSize = new(120, 40);

    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private static GitClientDialog CreateDialog(RecordingGitService gitService)
    {
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.SetupGet(m => m.Url).Returns(new Url(Path.GetTempPath()));

        var dateTimeProviderService = new Mock<IDateTimeProviderService>();
        dateTimeProviderService.SetupGet(m => m.Now).Returns(DateTime.Now);

        return new GitClientDialog(
            gitService,
            contentModelService.Object,
            new Mock<IBeepService>().Object,
            dateTimeProviderService.Object,
            CreateThemeService());
    }

    /// <summary>
    /// Shows the dialog and waits for its first background load to land. The dialog deliberately
    /// loads on a task rather than in <c>OnInitializeAsync</c>, so <see cref="GitClientDialog.PendingOperation"/>
    /// is what there is to wait on.
    /// </summary>
    private static async Task<Control> ShowAsync(GitClientDialog dialog)
    {
        var root = new Control(CreateThemeService()) { Name = "Root", ViewSize = ScreenSize };
        await root.ShowDialogAsync(dialog);
        await dialog.PendingOperation;
        return root;
    }

    /// <summary>Sends a key and waits for whatever background work it kicked off.</summary>
    private static async Task PressAsync(GitClientDialog dialog, KeyEvent keyEvent)
    {
        await dialog.HandleKeyEventAsync(keyEvent);
        await dialog.PendingOperation;

        // The app runs an idle pass between keystrokes, and the dialog re-syncs the section it
        // follows with the focus there; without it a test could press the next key in a state the
        // app never reaches.
        dialog.OnIdle();
    }

    private static string Render(GitClientDialog dialog)
    {
        var buffer = new MockBuffer(ScreenSize);
        dialog.Draw(buffer);
        return buffer.ContentAsString;
    }

    private static void AssertCalled(RecordingGitService gitService, string call) =>
        Assert.IsTrue(gitService.Calls.Contains(call),
            $"expected the '{call}' call. Recorded: {string.Join(", ", gitService.Calls)}");

    private static void Contains(string haystack, string needle, string? message = null) =>
        Assert.IsTrue(haystack.Contains(needle, StringComparison.Ordinal),
            $"{message ?? "expected the screen to contain"}: '{needle}'.{Environment.NewLine}Screen was:{Environment.NewLine}{haystack}");

    private static KeyEvent Key(char keyChar, ConsoleKey consoleKey, bool shift = false) =>
        new(keyChar, consoleKey, shift, false, false);

    private static GitRepositoryStatus StatusWith(
        IReadOnlyList<GitFileStatus>? unstaged = null,
        IReadOnlyList<GitFileStatus>? staged = null,
        string? upstream = null) =>
        new("main", upstream, 0, 0, staged ?? [], unstaged ?? []);

    private static GitFileStatus Modified(string path) => new(path, " M", IsUntracked: false);

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterStagesTheFileUnderTheCursor()
    {
        // Space and Enter are both advertised as the primary action, but Enter used to reach the
        // list box's own select-item command first and never got to the dialog.
        var gitService = new RecordingGitService { Status = StatusWith(unstaged: [Modified("src/Foo.cs")]) };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('\0', ConsoleKey.Enter));

        AssertCalled(gitService, "stage:src/Foo.cs");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task StageAllStagesEveryChangeFromTheUnstagedPanel()
    {
        var gitService = new RecordingGitService
        {
            Status = StatusWith(unstaged: [Modified("a.cs"), Modified("b.cs"), Modified("c.cs")]),
        };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('a', ConsoleKey.A));

        AssertCalled(gitService, "stage-all");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task StageAllUnstagesEverythingWhenTheStagingAreaIsFocused()
    {
        var gitService = new RecordingGitService
        {
            Status = StatusWith(staged: [Modified("a.cs"), Modified("b.cs")]),
        };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('\t', ConsoleKey.Tab));
        await PressAsync(dialog, Key('a', ConsoleKey.A));

        AssertCalled(gitService, "unstage-all");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PushPublishesABranchThatHasNoUpstream()
    {
        // A plain `git push` on an unpublished branch answers with a paragraph of advice rather than
        // pushing, which is never what the person who pressed P wanted.
        var gitService = new RecordingGitService { Status = StatusWith(upstream: null) };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('P', ConsoleKey.P, shift: true));

        AssertCalled(gitService, "push:set-upstream=True");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PushLeavesTheUpstreamAloneWhenTheBranchAlreadyHasOne()
    {
        var gitService = new RecordingGitService { Status = StatusWith(upstream: "origin/main") };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('P', ConsoleKey.P, shift: true));

        AssertCalled(gitService, "push:set-upstream=False");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheHelpOverlayListsTheKeysAndTakesTheWholeDialog()
    {
        var gitService = new RecordingGitService { Status = StatusWith(unstaged: [Modified("a.cs")]) };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('?', ConsoleKey.Oem2));

        var help = Render(dialog);
        Contains(help, "MOVING AROUND");
        Contains(help, "Stage everything");
        Assert.IsFalse(help.Contains("Unstaged (1)", StringComparison.Ordinal),
            "the panels are hidden while help is up — they are separate controls rendered in no guaranteed order, so they cannot be drawn over");

        // Any other key dismisses it.
        await PressAsync(dialog, Key('\0', ConsoleKey.Escape));
        Contains(Render(dialog), "Unstaged (1)");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EmptySectionsCostAHeaderRatherThanAFifthOfTheColumn()
    {
        var unstaged = Enumerable.Range(0, 30).Select(index => Modified($"file{index:00}.cs")).ToList();
        var gitService = new RecordingGitService { Status = StatusWith(unstaged: unstaged) };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        var screen = Render(dialog);

        // A fixed fifth of this dialog would show about six of the thirty files. With the staging
        // area, branches and stashes all empty, their rows belong to the list that has content.
        Contains(screen, "file20.cs",
            "the unstaged list should grow into the space the empty sections do not need");

        // Collapsed does not mean gone: every section keeps its header, so all five stay countable
        // and reachable with Tab.
        foreach (var header in new[] { "Staged (0)", "Branches (0)", "Commits (0)", "Stashes (0)" })
        {
            Contains(screen, header);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptyPanelSaysSoOnceTheCursorIsInIt()
    {
        var gitService = new RecordingGitService { Status = StatusWith(unstaged: [Modified("a.cs")]) };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        // Tabbing into the staging area when nothing is staged has to explain the blank, or the
        // collapsed section reads as a drawing glitch.
        await PressAsync(dialog, Key('\t', ConsoleKey.Tab));

        Contains(Render(dialog), "(nothing staged)");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedCommandShowsItsWholeOutputInTheDetailPane()
    {
        var gitService = new RecordingGitService { Status = StatusWith(upstream: "origin/main") };
        gitService.Failures["push:set-upstream=False"] =
            "To github.com:example/repo.git\n ! [rejected] main -> main (fetch first)\nhint: Updates were rejected.";
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('P', ConsoleKey.P, shift: true));

        // The footer can only carry the first line; the rest is what actually says what to do next.
        var screen = Render(dialog);
        Contains(screen, "hint: Updates were rejected.");
        Contains(screen, "! [rejected] main -> main (fetch first)");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheFooterShowsTheKeysForThePanelUnderTheCursor()
    {
        var gitService = new RecordingGitService { Status = StatusWith(unstaged: [Modified("a.cs")]) };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        // Showing the dialog makes every child control visible, the inline input included; a footer
        // that reads that as "the input is up" never draws a hint at all.
        Contains(Render(dialog), "?:keys");
        Contains(Render(dialog), "a:stage-all");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CheckingOutARemoteBranchCreatesALocalOneTrackingIt()
    {
        var gitService = new RecordingGitService
        {
            Branches =
            [
                new GitBranch("main", IsCurrent: true, "origin/main"),
                new GitBranch("origin/feature", IsCurrent: false, null, IsRemote: true),
            ],
        };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        // Tab twice: unstaged -> staged -> branches, then move onto the remote row.
        await PressAsync(dialog, Key('\t', ConsoleKey.Tab));
        await PressAsync(dialog, Key('\t', ConsoleKey.Tab));
        await PressAsync(dialog, Key('\0', ConsoleKey.DownArrow));
        await PressAsync(dialog, Key(' ', ConsoleKey.Spacebar));

        AssertCalled(gitService, "checkout-remote:origin/feature");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DiscardingAsksBeforeItThrowsWorkAway()
    {
        var gitService = new RecordingGitService { Status = StatusWith(unstaged: [Modified("a.cs")]) };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('d', ConsoleKey.D));
        Contains(Render(dialog), "Throw away your changes to a.cs?");
        Assert.AreEqual(0, gitService.Calls.Count, "nothing should run until the question is answered");

        // A stray key is neither a yes nor a no.
        await PressAsync(dialog, Key('q', ConsoleKey.Q));
        Contains(Render(dialog), "Throw away your changes to a.cs?");

        await PressAsync(dialog, Key('y', ConsoleKey.Y));
        AssertCalled(gitService, "discard:a.cs");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AmendOffersThePreviousMessageBackRatherThanABlankLine()
    {
        var gitService = new RecordingGitService
        {
            Status = StatusWith(staged: [Modified("a.cs")]),
            LastCommitMessage = "Fix the thing\n\nWith a body.",
        };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('C', ConsoleKey.C, shift: true));

        // Only the subject: the input is one line, and pre-filling a body would silently drop it.
        var input = dialog.Controls.AsSnapshot().OfType<TextBox>().Single();
        Assert.AreEqual("Fix the thing", input.Text);
        Assert.AreEqual("Amend last commit", input.Label);
        Contains(Render(dialog), "Fix the thing");

        await PressAsync(dialog, Key('\0', ConsoleKey.Enter));
        AssertCalled(gitService, "commit:amend:Fix the thing");
    }

    // ── Sections, branches, stashes, and the rest of the keys ──────────────────────────────────

    /// <summary>Moves focus <paramref name="tabs"/> sections on: Unstaged, Staged, Branches, Commits, Stashes.</summary>
    private static async Task TabAsync(GitClientDialog dialog, int tabs)
    {
        for (var i = 0; i < tabs; i++)
        {
            await PressAsync(dialog, Key('\t', ConsoleKey.Tab));
        }
    }

    private static async Task TypeAsync(GitClientDialog dialog, string text)
    {
        foreach (var c in text)
        {
            await dialog.HandleKeyEventAsync(new KeyEvent(c, ConsoleKey.A, char.IsUpper(c), false, false));
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NCreatesABranchFromTheTypedName()
    {
        var gitService = new RecordingGitService();
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('n', ConsoleKey.N));
        Contains(Render(dialog), "New branch");
        await TypeAsync(dialog, "feature");
        await PressAsync(dialog, Key('\r', ConsoleKey.Enter));

        AssertCalled(gitService, "create-branch:feature");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptyBranchNameCreatesNothing()
    {
        var gitService = new RecordingGitService();
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('n', ConsoleKey.N));
        await PressAsync(dialog, Key('\r', ConsoleKey.Enter));

        Assert.IsFalse(gitService.Calls.Any(c => c.StartsWith("create-branch", StringComparison.Ordinal)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscapeInTheInputCancelsItAndKeepsTheDialog()
    {
        var gitService = new RecordingGitService();
        var dialog = CreateDialog(gitService);
        var root = await ShowAsync(dialog);

        await PressAsync(dialog, Key('n', ConsoleKey.N));
        await PressAsync(dialog, Key('\x1b', ConsoleKey.Escape));

        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog));
        Contains(Render(dialog), "Cancelled.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnABranchChecksItOut()
    {
        var gitService = new RecordingGitService
        {
            Branches = [new GitBranch("main", IsCurrent: true, "origin/main"), new GitBranch("dev", IsCurrent: false, null)],
        };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);
        await TabAsync(dialog, 2);

        await PressAsync(dialog, Key('\0', ConsoleKey.DownArrow));
        await PressAsync(dialog, Key('\r', ConsoleKey.Enter));

        AssertCalled(gitService, "checkout:dev");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnARemoteBranchTracksIt()
    {
        var gitService = new RecordingGitService
        {
            Branches = [new GitBranch("main", IsCurrent: true, null), new GitBranch("origin/feature", IsCurrent: false, null, IsRemote: true)],
        };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);
        await TabAsync(dialog, 2);

        await PressAsync(dialog, Key('\0', ConsoleKey.DownArrow));
        await PressAsync(dialog, Key('\r', ConsoleKey.Enter));

        AssertCalled(gitService, "checkout-remote:origin/feature");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MMergesTheBranchUnderTheCursor()
    {
        var gitService = new RecordingGitService
        {
            Branches = [new GitBranch("main", IsCurrent: true, null), new GitBranch("dev", IsCurrent: false, null)],
        };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);
        await TabAsync(dialog, 2);

        await PressAsync(dialog, Key('\0', ConsoleKey.DownArrow));
        await PressAsync(dialog, Key('m', ConsoleKey.M));

        AssertCalled(gitService, "merge:dev");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DeletingABranchAsksFirstAndIgnoresStrayKeys()
    {
        var gitService = new RecordingGitService
        {
            Branches = [new GitBranch("main", IsCurrent: true, null), new GitBranch("old", IsCurrent: false, null)],
        };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);
        await TabAsync(dialog, 2);
        await PressAsync(dialog, Key('\0', ConsoleKey.DownArrow));

        await PressAsync(dialog, Key('D', ConsoleKey.D, shift: true));
        Contains(Render(dialog), "Delete branch old");
        await PressAsync(dialog, Key('q', ConsoleKey.Q));
        Assert.IsFalse(gitService.Calls.Any(c => c.StartsWith("delete-branch", StringComparison.Ordinal)), "a stray key is not an answer");

        await PressAsync(dialog, Key('y', ConsoleKey.Y));

        AssertCalled(gitService, "delete-branch:old");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task StashingAWorkingTreeWithChangesAsksForAMessage()
    {
        var gitService = new RecordingGitService { Status = StatusWith(unstaged: [Modified("a.cs")]) };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('S', ConsoleKey.S, shift: true));
        await TypeAsync(dialog, "wip");
        await PressAsync(dialog, Key('\r', ConsoleKey.Enter));

        AssertCalled(gitService, "stash-push:wip");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task StashingACleanTreeSaysThereIsNothingToStash()
    {
        var gitService = new RecordingGitService();
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('S', ConsoleKey.S, shift: true));

        Contains(Render(dialog), "Nothing to stash");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task InTheStashesSectionEnterPopsAAppliesAndXDropsAfterAsking()
    {
        var gitService = new RecordingGitService { Stashes = [new GitStash(0, "wip")] };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);
        await TabAsync(dialog, 4);

        await PressAsync(dialog, Key('a', ConsoleKey.A));
        AssertCalled(gitService, "stash-apply:0");

        await PressAsync(dialog, Key('\r', ConsoleKey.Enter));
        AssertCalled(gitService, "stash-pop:0");

        await PressAsync(dialog, Key('x', ConsoleKey.X));
        await PressAsync(dialog, Key('y', ConsoleKey.Y));
        AssertCalled(gitService, "stash-drop:0");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DiscardingAFileAsksFirst()
    {
        var gitService = new RecordingGitService { Status = StatusWith(unstaged: [Modified("a.cs")]) };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('d', ConsoleKey.D));
        Contains(Render(dialog), "Throw away your changes to a.cs?");
        await PressAsync(dialog, Key('n', ConsoleKey.N));
        Assert.IsFalse(gitService.Calls.Contains("discard:a.cs"));

        await PressAsync(dialog, Key('d', ConsoleKey.D));
        await PressAsync(dialog, Key('y', ConsoleKey.Y));

        AssertCalled(gitService, "discard:a.cs");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AmendIsPrefilledWithTheLastSubject()
    {
        var gitService = new RecordingGitService
        {
            Status = StatusWith(staged: [Modified("a.cs")]),
            LastCommitMessage = "Fix the thing" + "\n" + "\n" + "Body text",
        };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('C', ConsoleKey.C, shift: true));
        await PressAsync(dialog, Key('\r', ConsoleKey.Enter));

        AssertCalled(gitService, "commit:amend:Fix the thing");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AmendWithNoCommitSaysSo()
    {
        var gitService = new RecordingGitService();
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('C', ConsoleKey.C, shift: true));

        Contains(Render(dialog), "Nothing to amend");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task FAndPFetchAndPull()
    {
        var gitService = new RecordingGitService();
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('f', ConsoleKey.F));
        await PressAsync(dialog, Key('p', ConsoleKey.P));

        AssertCalled(gitService, "fetch");
        AssertCalled(gitService, "pull");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CapitalAAbortsAMergeOnlyWhileOneIsInProgress()
    {
        var gitService = new RecordingGitService();
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('A', ConsoleKey.A, shift: true));
        Assert.IsFalse(gitService.Calls.Contains("abort-merge"));

        gitService.MergeInProgress = true;
        await PressAsync(dialog, Key('r', ConsoleKey.R));
        await PressAsync(dialog, Key('A', ConsoleKey.A, shift: true));

        AssertCalled(gitService, "abort-merge");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HelpOverlayScrollsAndAnyOtherKeyClosesItButNotTheDialog()
    {
        var gitService = new RecordingGitService();
        var dialog = CreateDialog(gitService);
        var root = await ShowAsync(dialog);

        await PressAsync(dialog, Key('?', ConsoleKey.Oem2));
        await PressAsync(dialog, Key('\0', ConsoleKey.DownArrow));
        await PressAsync(dialog, Key('\0', ConsoleKey.PageDown));
        await PressAsync(dialog, Key('\0', ConsoleKey.Home));
        await PressAsync(dialog, Key('\x1b', ConsoleKey.Escape));

        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog), "Esc closes the help, not the client");

        await PressAsync(dialog, Key('\x1b', ConsoleKey.Escape));
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PageDownScrollsTheDiffPane()
    {
        var gitService = new RecordingGitService { Status = StatusWith(unstaged: [Modified("a.cs")]) };
        var dialog = CreateDialog(gitService);
        await ShowAsync(dialog);

        await PressAsync(dialog, Key('\0', ConsoleKey.PageDown));
        await PressAsync(dialog, Key('\0', ConsoleKey.PageUp));

        // The diff is three lines; nothing to scroll past, and nothing may throw.
        Contains(Render(dialog), "+changed");
    }
}

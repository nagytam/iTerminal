using iTerminal.Core.Model.Git;
using System.Diagnostics;

namespace iTerminal.Core.Services.Git;

/// <summary>
/// <see cref="IGitService"/> backed by the git CLI. Reads use machine-readable porcelain
/// formats; writes are serialized through <see cref="_writeLock"/> so two mutating commands
/// can't collide on <c>index.lock</c>.
/// </summary>
public sealed class GitService : IGitService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private const char UnitSeparator = '';   // between log fields
    private const char RecordSeparator = ''; // between log records

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task<string?> GetRepositoryRootAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(workingDirectory, cancellationToken, "rev-parse", "--show-toplevel");
        if (!result.Success)
        {
            return null;
        }

        var root = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .Trim();

        return string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)
            ? null
            : Path.GetFullPath(root);
    }

    public async Task<GitRepositoryStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(repositoryRoot, cancellationToken,
            "status", "--porcelain=v1", "--branch", "-z", "--untracked-files=all");
        if (!result.Success)
        {
            return GitRepositoryStatus.Empty;
        }

        return ParseStatus(result.StandardOutput);
    }

    public async Task<IReadOnlyList<GitCommit>> GetLogAsync(string repositoryRoot, int maxCount, CancellationToken cancellationToken = default)
    {
        var format = $"%h{UnitSeparator}%s{UnitSeparator}%an{UnitSeparator}%ar{RecordSeparator}";
        var result = await RunAsync(repositoryRoot, cancellationToken,
            "log", $"-n{Math.Max(1, maxCount)}", "--pretty=format:" + format);
        if (!result.Success)
        {
            return [];
        }

        var commits = new List<GitCommit>();
        foreach (var record in result.StandardOutput.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Trim('\r', '\n').Split(UnitSeparator);
            if (fields.Length >= 4)
            {
                commits.Add(new GitCommit(fields[0], fields[1], fields[2], fields[3]));
            }
        }

        return commits;
    }

    public async Task<string> GetDiffAsync(string repositoryRoot, string relativePath, bool staged, bool isUntracked, CancellationToken cancellationToken = default)
    {
        if (isUntracked)
        {
            // Untracked files have no diff; show their content via an intent-to-add diff.
            var added = await RunAsync(repositoryRoot, cancellationToken,
                "diff", "--no-index", "--", NullDevice, relativePath);
            return string.IsNullOrWhiteSpace(added.StandardOutput)
                ? "(untracked file)"
                : added.StandardOutput;
        }

        var args = staged
            ? new[] { "diff", "--cached", "--", relativePath }
            : ["diff", "--", relativePath];
        var result = await RunAsync(repositoryRoot, cancellationToken, args);
        return string.IsNullOrWhiteSpace(result.StandardOutput)
            ? "(no changes)"
            : result.StandardOutput;
    }

    public async Task<string> GetCommitDiffAsync(string repositoryRoot, string commit, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(repositoryRoot, cancellationToken, "show", "--stat", "--patch", commit);
        return string.IsNullOrWhiteSpace(result.StandardOutput) ? "(no details)" : result.StandardOutput;
    }

    public Task<GitResult> StageAsync(string repositoryRoot, string relativePath, CancellationToken cancellationToken = default) =>
        RunWriteAsync(repositoryRoot, cancellationToken, "add", "--", relativePath);

    public Task<GitResult> StageAllAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
        RunWriteAsync(repositoryRoot, cancellationToken, "add", "-A", "--");

    public Task<GitResult> UnstageAsync(string repositoryRoot, string relativePath, CancellationToken cancellationToken = default) =>
        RunWriteAsync(repositoryRoot, cancellationToken, "reset", "-q", "HEAD", "--", relativePath);

    public Task<GitResult> UnstageAllAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
        RunWriteAsync(repositoryRoot, cancellationToken, "reset", "-q", "HEAD", "--", ".");

    public Task<GitResult> DiscardAsync(string repositoryRoot, string relativePath, bool isUntracked, CancellationToken cancellationToken = default) =>
        isUntracked
            ? RunWriteAsync(repositoryRoot, cancellationToken, "clean", "-f", "--", relativePath)
            : RunWriteAsync(repositoryRoot, cancellationToken, "checkout", "--", relativePath);

    public Task<GitResult> CommitAsync(string repositoryRoot, string message, bool amend, CancellationToken cancellationToken = default) =>
        amend
            ? RunWriteAsync(repositoryRoot, cancellationToken, "commit", "--amend", "-m", message)
            : RunWriteAsync(repositoryRoot, cancellationToken, "commit", "-m", message);

    public async Task<string> GetLastCommitMessageAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(repositoryRoot, cancellationToken, "log", "-1", "--pretty=format:%B");
        return result.Success ? result.StandardOutput.Trim('\r', '\n') : string.Empty;
    }

    public async Task<IReadOnlyList<GitBranch>> GetBranchesAsync(string repositoryRoot, bool includeRemotes = false, CancellationToken cancellationToken = default)
    {
        var locals = await ReadBranchRefsAsync(repositoryRoot, "refs/heads", isRemote: false, cancellationToken);
        if (!includeRemotes)
        {
            return locals;
        }

        // Two calls rather than one over both ref spaces: for-each-ref prints short names, so a local
        // branch literally called "origin/x" would be indistinguishable from a remote-tracking one.
        var remotes = await ReadBranchRefsAsync(repositoryRoot, "refs/remotes", isRemote: true, cancellationToken);
        var localNames = locals.Select(branch => branch.Name).ToHashSet(StringComparer.Ordinal);

        // Locals first — they are what a checkout normally means, and the remote block can be long.
        // A remote branch already checked out locally is dropped: its local row does the same job.
        return
        [
            .. locals,
            .. remotes.Where(branch => !localNames.Contains(StripRemote(branch.Name))),
        ];
    }

    private static async Task<List<GitBranch>> ReadBranchRefsAsync(string repositoryRoot, string refSpace, bool isRemote, CancellationToken cancellationToken)
    {
        // %(HEAD) is '*' for the current branch, ' ' otherwise; NUL separates name from upstream.
        var result = await RunAsync(repositoryRoot, cancellationToken,
            "for-each-ref", "--format=%(HEAD)%(refname:short)%00%(upstream:short)", refSpace);
        if (!result.Success)
        {
            return [];
        }

        var branches = new List<GitBranch>();
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Replace("\r", string.Empty).Split('\0');
            if (fields.Length == 0 || fields[0].Length < 1)
            {
                continue;
            }

            var isCurrent = fields[0][0] == '*';
            var name = fields[0][1..].Trim();
            var upstream = fields.Length > 1 && fields[1].Length > 0 ? fields[1] : null;

            // "origin/HEAD" is a symbolic ref, not a branch anyone can check out.
            if (name.Length == 0 || (isRemote && name.EndsWith("/HEAD", StringComparison.Ordinal)))
            {
                continue;
            }

            branches.Add(new GitBranch(name, isCurrent && !isRemote, upstream, isRemote));
        }

        return branches;
    }

    /// <summary>"origin/feature" -> "feature"; the local name a remote-tracking branch would be checked out as.</summary>
    private static string StripRemote(string remoteBranchName)
    {
        var slash = remoteBranchName.IndexOf('/');
        return slash >= 0 ? remoteBranchName[(slash + 1)..] : remoteBranchName;
    }

    public Task<GitResult> CheckoutBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default) =>
        RunWriteAsync(repositoryRoot, cancellationToken, "checkout", branchName);

    public async Task<GitResult> CheckoutRemoteBranchAsync(string repositoryRoot, string remoteBranchName, CancellationToken cancellationToken = default)
    {
        var result = await RunWriteAsync(repositoryRoot, cancellationToken, "checkout", "--track", remoteBranchName);
        if (result.Success)
        {
            return result;
        }

        // --track fails when the local branch already exists; checking it out is what the user meant.
        var localName = StripRemote(remoteBranchName);
        return localName.Length == 0
            ? result
            : await RunWriteAsync(repositoryRoot, cancellationToken, "checkout", localName);
    }

    public Task<GitResult> CreateBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default) =>
        RunWriteAsync(repositoryRoot, cancellationToken, "checkout", "-b", branchName);

    public Task<GitResult> MergeBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default) =>
        RunWriteAsync(repositoryRoot, cancellationToken, "merge", "--no-edit", branchName);

    public Task<GitResult> DeleteBranchAsync(string repositoryRoot, string branchName, bool force, CancellationToken cancellationToken = default) =>
        RunWriteAsync(repositoryRoot, cancellationToken, "branch", force ? "-D" : "-d", branchName);

    public Task<GitResult> AbortMergeAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
        RunWriteAsync(repositoryRoot, cancellationToken, "merge", "--abort");

    public async Task<bool> IsMergeInProgressAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(repositoryRoot, cancellationToken, "rev-parse", "--verify", "--quiet", "MERGE_HEAD");
        return result.Success;
    }

    public Task<GitResult> FetchAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
        RunWriteAsync(repositoryRoot, cancellationToken, "fetch", "--all", "--prune");

    public Task<GitResult> PullAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
        RunWriteAsync(repositoryRoot, cancellationToken, "pull");

    public async Task<GitResult> PushAsync(string repositoryRoot, bool setUpstream = false, CancellationToken cancellationToken = default)
    {
        if (!setUpstream)
        {
            return await RunWriteAsync(repositoryRoot, cancellationToken, "push");
        }

        var remote = await GetDefaultRemoteAsync(repositoryRoot, cancellationToken);
        return remote is null
            ? GitResult.Fail("No remote configured — add one with 'git remote add origin <url>'.")
            : await RunWriteAsync(repositoryRoot, cancellationToken, "push", "--set-upstream", remote, "HEAD");
    }

    public async Task<string?> GetDefaultRemoteAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(repositoryRoot, cancellationToken, "remote");
        if (!result.Success)
        {
            return null;
        }

        var remotes = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return remotes.FirstOrDefault(remote => remote == "origin") ?? remotes.FirstOrDefault();
    }

    public async Task<IReadOnlyList<GitStash>> GetStashesAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(repositoryRoot, cancellationToken, "stash", "list", "--format=%gs");
        if (!result.Success)
        {
            return [];
        }

        var stashes = new List<GitStash>();
        var lines = result.StandardOutput.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // List order matches stash@{0}, stash@{1}, ... (newest first).
        for (var i = 0; i < lines.Length; i++)
        {
            stashes.Add(new GitStash(i, lines[i]));
        }

        return stashes;
    }

    public Task<GitResult> StashPushAsync(string repositoryRoot, string? message = null, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(message)
            ? RunWriteAsync(repositoryRoot, cancellationToken, "stash", "push", "--include-untracked")
            : RunWriteAsync(repositoryRoot, cancellationToken, "stash", "push", "--include-untracked", "-m", message.Trim());

    public Task<GitResult> StashPopAsync(string repositoryRoot, int index, CancellationToken cancellationToken = default) =>
        RunWriteAsync(repositoryRoot, cancellationToken, "stash", "pop", $"stash@{{{index}}}");

    public Task<GitResult> StashApplyAsync(string repositoryRoot, int index, CancellationToken cancellationToken = default) =>
        RunWriteAsync(repositoryRoot, cancellationToken, "stash", "apply", $"stash@{{{index}}}");

    public Task<GitResult> StashDropAsync(string repositoryRoot, int index, CancellationToken cancellationToken = default) =>
        RunWriteAsync(repositoryRoot, cancellationToken, "stash", "drop", $"stash@{{{index}}}");

    private static string NullDevice => OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

    private async Task<GitResult> RunWriteAsync(string repositoryRoot, CancellationToken cancellationToken, params string[] args)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var result = await RunAsync(repositoryRoot, cancellationToken, args);
            return result.Success
                ? GitResult.Ok(result.StandardOutput)
                : GitResult.Fail(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static GitRepositoryStatus ParseStatus(string output)
    {
        var branch = string.Empty;
        string? upstream = null;
        var ahead = 0;
        var behind = 0;
        var staged = new List<GitFileStatus>();
        var unstaged = new List<GitFileStatus>();

        // -z separates records with NUL. Rename/copy entries are followed by a second NUL
        // field holding the original path, which we skip.
        var records = output.Split('\0');
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.Length == 0)
            {
                continue;
            }

            if (record.StartsWith("## ", StringComparison.Ordinal))
            {
                ParseBranchHeader(record[3..], out branch, out upstream, out ahead, out behind);
                continue;
            }

            if (record.Length < 3)
            {
                continue;
            }

            var code = record[..2];
            var path = record[3..];
            var isUntracked = code == "??";

            // A rename/copy on either side consumes the following original-path field.
            if (!isUntracked && (code[0] is 'R' or 'C' || code[1] is 'R' or 'C'))
            {
                i++;
            }

            var entry = new GitFileStatus(path, code, isUntracked);
            if (entry.HasStagedChange)
            {
                staged.Add(entry);
            }
            if (entry.HasWorkTreeChange)
            {
                unstaged.Add(entry);
            }
        }

        return new GitRepositoryStatus(branch, upstream, ahead, behind, staged, unstaged);
    }

    private static void ParseBranchHeader(string header, out string branch, out string? upstream, out int ahead, out int behind)
    {
        upstream = null;
        ahead = 0;
        behind = 0;

        // Examples:
        //   "main"
        //   "main...origin/main"
        //   "main...origin/main [ahead 2]"
        //   "main...origin/main [ahead 2, behind 1]"
        //   "No commits yet on main"
        var bracketIndex = header.IndexOf(" [", StringComparison.Ordinal);
        var headPart = bracketIndex >= 0 ? header[..bracketIndex] : header;

        var trackingIndex = headPart.IndexOf("...", StringComparison.Ordinal);
        if (trackingIndex >= 0)
        {
            branch = headPart[..trackingIndex].Trim();
            upstream = headPart[(trackingIndex + 3)..].Trim();
        }
        else
        {
            branch = headPart.Replace("No commits yet on ", string.Empty).Trim();
        }

        if (bracketIndex >= 0)
        {
            var inside = header[(bracketIndex + 2)..].TrimEnd(']');
            foreach (var part in inside.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 2 && int.TryParse(tokens[1], out var value))
                {
                    if (tokens[0] == "ahead") ahead = value;
                    else if (tokens[0] == "behind") behind = value;
                }
            }
        }
    }

    private static async Task<ProcessResult> RunAsync(string workingDirectory, CancellationToken cancellationToken, params string[] args)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // Never let git block on an interactive credential/passphrase prompt — there is no
            // TTY here, so a prompt would hang until the timeout. Fail fast instead so push/pull
            // surface a clear error and rely on the configured credential helper for auth.
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            // Drain both pipes concurrently so a full stderr buffer can't deadlock a full stdout.
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new ProcessResult(false, string.Empty, "git command timed out");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return new ProcessResult(process.ExitCode == 0, stdout, stderr);
        }
        catch (Exception exception)
        {
            return new ProcessResult(false, string.Empty, exception.Message);
        }
    }

    private readonly record struct ProcessResult(bool Success, string StandardOutput, string StandardError);
}

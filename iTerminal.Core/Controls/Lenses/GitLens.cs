using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model.Git;
using iTerminal.Core.Services.Git;

namespace iTerminal.Core.Controls.Lenses;

/// <summary>
/// Decorates file-panel rows with their git working-tree status and shows a "{Git}" header
/// indicator. State is computed on a background task via <see cref="IGitService"/> — git is
/// never run from the render thread (see <c>DecorateRow</c>/<c>GetHeaderIndicator</c>, which
/// only read cached state).
/// </summary>
public class GitLens(IGitService gitService) : IContentLens
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);
    private readonly object _sync = new();
    private string _lastWorkingDirectory = string.Empty;
    private DateTime _lastRefreshUtc = DateTime.MinValue;
    private bool _isRepository;
    private SyncState? _syncState;
    private Dictionary<string, GitEntryState> _entryStates = [];
    private bool _refreshInFlight;

    public string? GetHeaderIndicator(IContentModel contentModel)
    {
        if (!TryGetWorkingDirectory(contentModel, out string workingDirectory))
        {
            return null;
        }

        // Never block the render thread: schedule a background refresh and read cached state.
        ScheduleRefreshIfNeeded(workingDirectory);
        lock (_sync)
        {
            if (!_isRepository
                || !string.Equals(_lastWorkingDirectory, workingDirectory, StringComparison.Ordinal))
            {
                return null;
            }

            // "{Git 1/2}": one commit on the remote that is not here yet, two here that are not on the remote.
            // The counts come from the last-known remote-tracking branch; nothing here fetches.
            return _syncState is { } sync ? $"{{Git {sync.Behind}/{sync.Ahead}}}" : "{Git}";
        }
    }

    public void DecorateRow(
        IContentModel contentModel,
        IEntry? entry,
        ConxelString row,
        bool cursor,
        ColorScheme colorScheme,
        VisualState visualState)
    {
        _ = colorScheme;
        _ = visualState;

        if (cursor || entry is null || entry.Url.PathType != PathType.File)
        {
            return;
        }

        if (!TryGetWorkingDirectory(contentModel, out string workingDirectory))
        {
            return;
        }

        ScheduleRefreshIfNeeded(workingDirectory);

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(entry.Url.Path);
        }
        catch
        {
            return;
        }

        GitEntryState state;
        lock (_sync)
        {
            if (!_isRepository || !_entryStates.TryGetValue(fullPath, out state))
            {
                return;
            }
        }

        if (state.IsModified || state.IsUntracked)
        {
            for (int i = 0; i < row.Count; i++)
            {
                var cell = row.String[i];
                cell.BackgroundColor = Color.DarkBlue;
                row.String[i] = cell;
            }
        }
    }

    /// <summary>
    /// Decides whether git state is stale and, if so, kicks off a single background refresh.
    /// This method never runs git itself — it must stay cheap because it is called from the
    /// render thread for the header and for every visible row. The actual git work runs on a
    /// thread-pool thread in <see cref="RefreshStateAsync"/>.
    /// </summary>
    private void ScheduleRefreshIfNeeded(string workingDirectory)
    {
        string directoryToRefresh;
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            var sameDirectory = string.Equals(_lastWorkingDirectory, workingDirectory, StringComparison.Ordinal);

            if (!sameDirectory)
            {
                // Directory changed under the cursor: drop the previous repo's cached state
                // immediately so we never decorate entries with another folder's git status.
                _lastWorkingDirectory = workingDirectory;
                _isRepository = false;
                _syncState = null;
                _entryStates = [];
            }
            else if ((now - _lastRefreshUtc) < RefreshInterval)
            {
                return;
            }

            if (_refreshInFlight)
            {
                return;
            }

            _refreshInFlight = true;
            _lastRefreshUtc = now;
            directoryToRefresh = workingDirectory;
        }

        _ = Task.Run(() => RefreshStateAsync(directoryToRefresh));
    }

    /// <summary>
    /// Resolves the repo and its status off the render thread and publishes the result under the
    /// lock. Results are discarded if the user has since navigated to another directory.
    /// </summary>
    private async Task RefreshStateAsync(string workingDirectory)
    {
        try
        {
            var isRepository = false;
            var entryStates = new Dictionary<string, GitEntryState>();
            SyncState? syncState = null;

            var root = await gitService.GetRepositoryRootAsync(workingDirectory);
            if (root is not null)
            {
                isRepository = true;
                var status = await gitService.GetStatusAsync(root);
                AddEntries(status.Unstaged, root, entryStates);
                AddEntries(status.Staged, root, entryStates);
                if (status.Upstream is not null)
                {
                    syncState = new SyncState(status.Ahead, status.Behind);
                }
            }

            lock (_sync)
            {
                // Only publish if the cursor is still on the directory we refreshed.
                if (string.Equals(_lastWorkingDirectory, workingDirectory, StringComparison.Ordinal))
                {
                    _isRepository = isRepository;
                    _syncState = syncState;
                    _entryStates = entryStates;
                    _lastRefreshUtc = DateTime.UtcNow;
                }
            }
        }
        catch
        {
            // Ignore — a failed refresh just leaves the previous decorations in place.
        }
        finally
        {
            lock (_sync)
            {
                _refreshInFlight = false;
            }
        }
    }

    private static void AddEntries(IReadOnlyList<GitFileStatus> files, string repositoryRoot, Dictionary<string, GitEntryState> entryStates)
    {
        foreach (var file in files)
        {
            string fullPath;
            try
            {
                var relative = file.Path.Replace('/', Path.DirectorySeparatorChar);
                fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, relative));
            }
            catch
            {
                continue;
            }

            entryStates[fullPath] = new GitEntryState(!file.IsUntracked, file.IsUntracked);
        }
    }

    private static bool TryGetWorkingDirectory(IContentModel contentModel, out string workingDirectory)
    {
        workingDirectory = string.Empty;

        try
        {
            if (contentModel.Url.ContainerType != ContainerType.FileSystem)
            {
                return false;
            }

            workingDirectory = Path.GetFullPath(contentModel.Url.Path);
            return Directory.Exists(workingDirectory);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Commits the local branch is ahead of and behind its upstream; absent when the branch has none.</summary>
    private readonly record struct SyncState(int Ahead, int Behind);

    private readonly record struct GitEntryState(bool IsModified, bool IsUntracked);
}

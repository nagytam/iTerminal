using iConsole.Localization;
using iConsole.Commands;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Model.Git;
using iTerminal.Core.Services.Git;

namespace iTerminal.Core.Commands;

/// <summary>
/// Git sync: pulls the current branch's upstream with a rebase and then pushes the local commits, for the repository
/// the focused file panel is in. The pull stops the sync when it fails, so nothing is pushed on top of a half-finished
/// rebase. Offered only in a folder of a local working tree (see <see cref="GitPullRebaseCommand.IsInWorkingTree"/>).
/// Both git calls wait on the network, so they run on a task of their own and the outcome is a notification.
/// </summary>
public class GitSyncCommand(
    IContentModelService contentModelService,
    IGitService gitService,
    INotificationService? notificationService = null) : Command
{
    public override string Title => "Git sync";
    public override string Description => "Pull the current branch's upstream with a rebase, then push the local commits (the panel's folder is a git working tree).";

    /// <summary>The sync started last. Nothing waits on it; it exists so a test can.</summary>
    public Task SyncTask { get; private set; } = Task.CompletedTask;

    private string? Folder => GitPullRebaseCommand.ResolveFolder(contentModelService);

    public override bool CanExecute() => Folder is not null;

    public override Task ExecuteAsync(object? args)
    {
        string? folder = Folder;
        if (folder is null)
        {
            return Task.CompletedTask;
        }

        notificationService?.Notify("Git", UiText.Get("Notify.GitSyncing", "Syncing with the remote…"), NotificationSeverity.Info);
        SyncTask = Task.Run(async () =>
        {
            try
            {
                // The marker check is a cheap guess; git has the last word on where the repository is.
                string? root = await gitService.GetRepositoryRootAsync(folder);
                if (root is null)
                {
                    notificationService?.Notify("Git sync", UiText.Get("Notify.NotGitRepo", "This folder is not in a git repository."), NotificationSeverity.Warning);
                    return;
                }

                GitRepositoryStatus status = await gitService.GetStatusAsync(root);
                if (status.Upstream is null)
                {
                    notificationService?.Notify("Git sync", UiText.Get("Notify.GitSyncNoUpstream", "The branch has no upstream branch to sync with."), NotificationSeverity.Warning);
                    return;
                }

                GitResult pull = await gitService.PullRebaseAsync(root);
                if (!pull.Success)
                {
                    // A conflict leaves the rebase unfinished in the working tree; say so, since nothing here resolves it.
                    notificationService?.Notify(
                        UiText.Get("Notify.GitSyncPullFailed", "Git sync: pull --rebase failed"),
                        GitPullRebaseCommand.FirstLine(pull.Output) + " " + UiText.Get("Notify.GitSyncRebaseNote", "(Nothing was pushed. If git started a rebase, it is left unfinished.)"),
                        NotificationSeverity.Warning);
                    return;
                }

                GitResult push = await gitService.PushAsync(root);
                if (push.Success)
                {
                    notificationService?.Notify("Git", UiText.Get("Notify.GitSynced", "Synced with the remote."), NotificationSeverity.Info);
                }
                else
                {
                    notificationService?.Notify(UiText.Get("Notify.GitSyncPushFailed", "Git sync: push failed"), GitPullRebaseCommand.FirstLine(push.Output), NotificationSeverity.Warning);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Uncaught, the task would end with no word to the user after "Syncing…".
                notificationService?.Notify(UiText.Get("Notify.GitSyncFailed", "Git sync failed"), exception.Message, NotificationSeverity.Warning);
            }
        });

        return Task.CompletedTask;
    }
}

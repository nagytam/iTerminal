using iConsole.Localization;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services.Git;

namespace iTerminal.Core.Commands;

/// <summary>
/// Git pull --rebase: fetches the current branch's upstream and replays the local commits on top of it, for the
/// repository the focused file panel is in. Offered only in a folder of a local working tree (a <c>.git</c> entry in
/// the folder or above it — a few stat calls, so it is safe to ask from a menu or the palette). The pull runs on a task of its
/// own, since it waits on the network, and the outcome is a notification.
/// </summary>
public class GitPullRebaseCommand(
    IContentModelService contentModelService,
    IGitService gitService,
    INotificationService? notificationService = null) : Command
{
    public override string Title => "Git pull --rebase";
    public override string Description => "Pull the current branch's upstream and rebase the local commits onto it (the panel's folder is a git working tree).";

    /// <summary>The pull started last. Nothing waits on it; it exists so a test can.</summary>
    public Task PullTask { get; private set; } = Task.CompletedTask;

    private string? Folder => ResolveFolder(contentModelService);

    /// <summary>The local folder of the focused (else the last) panel when it is inside a working tree; shared with the sync command.</summary>
    internal static string? ResolveFolder(IContentModelService contentModelService)
    {
        IContentModel? model = contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();
        if (model is null || model.Url.ContainerType != ContainerType.FileSystem)
        {
            return null;
        }

        try
        {
            string path = Path.GetFullPath(model.Url.Path);
            return Directory.Exists(path) && IsInWorkingTree(path) ? path : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>True when <paramref name="folder"/> or one of its parents has a <c>.git</c> directory (or the file a worktree or submodule has).</summary>
    public static bool IsInWorkingTree(string folder)
    {
        for (DirectoryInfo? directory = new(folder); directory is not null; directory = directory.Parent)
        {
            string marker = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
            {
                return true;
            }
        }

        return false;
    }

    public override bool CanExecute() => Folder is not null;

    public override Task ExecuteAsync(object? args)
    {
        string? folder = Folder;
        if (folder is null)
        {
            return Task.CompletedTask;
        }

        notificationService?.Notify("Git", UiText.Get("Notify.PullingRebase", "Pulling with rebase…"), NotificationSeverity.Info);
        PullTask = Task.Run(async () =>
        {
            try
            {
                // The marker check is a cheap guess; git has the last word on where the repository is.
                string? root = await gitService.GetRepositoryRootAsync(folder);
                if (root is null)
                {
                    notificationService?.Notify("Git pull --rebase", UiText.Get("Notify.NotGitRepo", "This folder is not in a git repository."), NotificationSeverity.Warning);
                    return;
                }

                GitResult result = await gitService.PullRebaseAsync(root);
                if (result.Success)
                {
                    notificationService?.Notify("Git", UiText.Get("Notify.PulledRebase", "Pulled with rebase."), NotificationSeverity.Info);
                }
                else
                {
                    notificationService?.Notify(UiText.Get("Notify.PullRebaseFailed", "Git pull --rebase failed"), FirstLine(result.Output), NotificationSeverity.Warning);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Uncaught, the task would end with no word to the user after "Pulling…".
                notificationService?.Notify(UiText.Get("Notify.PullRebaseFailed", "Git pull --rebase failed"), exception.Message, NotificationSeverity.Warning);
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>A toast has room for one line; git's own explanation of a conflict or a missing upstream is the first.</summary>
    internal static string FirstLine(string output)
    {
        string line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        return line.Length == 0 ? "git returned an error." : line;
    }
}

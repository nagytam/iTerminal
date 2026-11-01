using iConsole.Localization;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Factories;
using iTerminal.Core.Helper;
using iTerminal.Core.Model.ContentModels;
using Serilog;

namespace iTerminal.Core.Services.Clipboard;

/// <summary>
/// Copy, cut and paste of files through the system clipboard (Ctrl+C, Ctrl+X, Ctrl+V and their
/// Ctrl+Insert, Shift+Delete, Shift+Insert twins): the rules that are not about a key.
///
/// <list type="bullet">
/// <item>Only entries on the local disk go onto the clipboard — another program is handed a path, so
/// a file in a container or an archive has nothing it could open. Whatever <see cref="FocusedEntry"/>
/// would act on (the marks, else the cursor row) is what is copied; marks are left standing.</item>
/// <item>Cut is a promise, not an act: nothing is touched until a paste, and what moves is whatever
/// still exists then.</item>
/// <item>Paste goes through the same copy and move tasks as F5 and Shift+F6, so it has their progress
/// window, cancellation, panel refresh and completion toast. Into the folder a file came from, a copy
/// gets Explorer's "name - Copy" rather than failing; a move there does nothing.</item>
/// <item>Paste never overwrites: there is no conflict dialog to ask in, and replacing a file on a
/// keystroke is the wrong default.</item>
/// </list>
/// </summary>
public class FileClipboardOperations(
    IFileClipboardService clipboard,
    IContentModelService contentModelService,
    FileSystemResolver fileSystemResolver,
    TaskManager taskManager,
    ProgressWindowFactory progressWindowFactory,
    IErrorPromptService errorPromptService,
    IConflictPromptService conflictPromptService,
    INotificationService notificationService,
    ILogger logger)
{
    /// <summary>The local paths of what a copy or cut in <paramref name="model"/> would take; empty when there are none.</summary>
    public IReadOnlyList<string> LocalPathsOf(IContentModel? model)
    {
        var paths = new List<string>();
        foreach (var url in FocusedEntry.ActionableUrls(model))
        {
            if (url.ContainerType == ContainerType.FileSystem && !string.IsNullOrEmpty(url.Path))
            {
                paths.Add(TrimPath(url.Path));
            }
        }

        return paths;
    }

    /// <summary>Whether <paramref name="model"/> is a folder a paste may write into.</summary>
    public bool CanPasteInto(IContentModel? model) =>
        model is not null
        && model is not IMusicContentModel
        && !model.IsReadOnly
        && model.Url is { } url
        && url.ContainerType is not (ContainerType.Docker or ContainerType.Git);

    /// <summary>Puts the entries of <paramref name="model"/> on the system clipboard; true when something was put there.</summary>
    public async Task<bool> CopyAsync(IContentModel? model, bool cut)
    {
        var paths = LocalPathsOf(model);
        if (paths.Count == 0)
        {
            return false;
        }

        try
        {
            await clipboard.SetFilesAsync(paths, cut);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Warning(exception, "Putting files on the clipboard failed");
            notificationService.Notify(UiText.Get("Notify.Clipboard", "Clipboard"), exception.Message, NotificationSeverity.Error);
            return false;
        }

        var what = paths.Count == 1 ? Path.GetFileName(paths[0]) : $"{paths.Count} items";
        notificationService.Notify(cut ? "Cut" : "Copied", what, NotificationSeverity.Info);
        return true;
    }

    /// <summary>
    /// Pastes the clipboard's files into the folder <paramref name="model"/> shows. A progress window
    /// is shown over <paramref name="host"/>; the result is announced by notification when it ends.
    /// </summary>
    public async Task PasteAsync(IContentModel? model, Control host)
    {
        if (!CanPasteInto(model) || model is null)
        {
            return;
        }

        FileClipboardContent? content;
        try
        {
            content = await clipboard.GetFilesAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Warning(exception, "Reading the clipboard failed");
            notificationService.Notify(UiText.Get("Notify.Clipboard", "Clipboard"), exception.Message, NotificationSeverity.Error);
            return;
        }

        if (content is null || content.Paths.Count == 0)
        {
            notificationService.Notify(UiText.Get("Notify.Paste", "Paste"), UiText.Get("Notify.ClipboardNoFiles", "The clipboard holds no files."), NotificationSeverity.Warning);
            return;
        }

        var plan = Plan(content, model.Url);
        if (plan.Sources.Count == 0)
        {
            if (plan.Skipped.Count > 0)
            {
                notificationService.Notify(UiText.Get("Notify.Paste", "Paste"), plan.Skipped[0], NotificationSeverity.Warning);
            }

            return;
        }

        var transfer = content.IsCut
            ? (TransferCommand)new MoveCommand(taskManager, progressWindowFactory, errorPromptService, conflictPromptService, fileSystemResolver, contentModelService)
            : new CopyCommand(taskManager, progressWindowFactory, errorPromptService, conflictPromptService, fileSystemResolver, contentModelService);
        await transfer.StartAsync(host, plan.Sources, plan.Targets, model.Url, FileConflictMode.Legacy);

        if (plan.Skipped.Count > 0)
        {
            notificationService.Notify(UiText.Get("Notify.Paste", "Paste"), string.Format(UiText.Get("Notify.SkippedCount", "{0} skipped: {1}"), plan.Skipped.Count, plan.Skipped[0]), NotificationSeverity.Warning);
        }
    }

    /// <summary>What a paste will do, worked out before anything is queued: pure over the disk, so a test can check it.</summary>
    internal PastePlan Plan(FileClipboardContent content, Url targetFolder)
    {
        var sources = new List<Url>();
        var targets = new List<Url>();
        var skipped = new List<string>();
        var targetSystem = fileSystemResolver.Resolve(targetFolder, operation: "Paste");
        var folderPath = TrimPath(targetFolder.FullUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? targetFolder.Path
            : targetFolder.FullUrl);

        foreach (var path in content.Paths)
        {
            var isFolder = Directory.Exists(path);
            if (!isFolder && !File.Exists(path))
            {
                skipped.Add($"{Path.GetFileName(path)} no longer exists");
                continue;
            }

            var name = Path.GetFileName(TrimPath(path));
            var sourceParent = Path.GetDirectoryName(TrimPath(path)) ?? string.Empty;
            var sameFolder = string.Equals(sourceParent, folderPath, StringComparison.OrdinalIgnoreCase);

            if (isFolder && IsInside(folderPath, path))
            {
                skipped.Add($"{name} cannot go into itself");
                continue;
            }

            if (content.IsCut && sameFolder)
            {
                skipped.Add($"{name} is already in this folder");
                continue;
            }

            var finalName = name;
            for (var n = 1; Exists(targetSystem, targetFolder, finalName, isFolder) && !content.IsCut; n++)
            {
                finalName = CopyName(name, isFolder, n);
            }

            var separator = Path.DirectorySeparatorChar;
            sources.Add(new Url(isFolder ? TrimPath(path) + separator : path));
            targets.Add(new Url(UrlPath.Child(targetFolder.FullUrl, finalName, isFolder)));
        }

        return new PastePlan(sources, targets, skipped);
    }

    internal sealed record PastePlan(IReadOnlyList<Url> Sources, IReadOnlyList<Url> Targets, IReadOnlyList<string> Skipped);

    /// <summary>Explorer's name for a copy beside its original: "a - Copy.txt", "a - Copy (2).txt".</summary>
    internal static string CopyName(string name, bool isFolder, int number)
    {
        var stem = isFolder ? name : Path.GetFileNameWithoutExtension(name);
        var extension = isFolder ? string.Empty : Path.GetExtension(name);
        return number <= 1 ? $"{stem} - Copy{extension}" : $"{stem} - Copy ({number}){extension}";
    }

    private static bool Exists(IFileSystem system, Url folder, string name, bool isFolder)
    {
        var url = new Url(UrlPath.Child(folder.FullUrl, name, isFolder));
        return isFolder ? system.ExistsFolder(url) : system.ExistsFile(url) || system.ExistsFolder(new Url(UrlPath.Child(folder.FullUrl, name, true)));
    }

    private static bool IsInside(string folder, string ancestor)
    {
        var a = TrimPath(ancestor) + Path.DirectorySeparatorChar;
        var f = TrimPath(folder) + Path.DirectorySeparatorChar;
        return f.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimPath(string path) =>
        path.Length > 3 ? path.TrimEnd('\\', '/') : path;
}

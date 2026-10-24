using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Model.ContentModels;
using System.ComponentModel;

namespace iTerminal.Core.Helper;

/// <summary>
/// Re-reads the panels a finished copy/move/delete touched.
///
/// Only <see cref="Model.ContentModels.ContentModelFileSystem"/> notices a change by itself, and
/// only because it runs a <see cref="FileSystemWatcher"/> over the folder it is showing. The memory
/// drive, SSH, SMB, iPhone and archive models have nothing watching them, so without this a panel
/// keeps drawing the listing it had before the operation until the user navigates away and back —
/// a file copied onto a <c>memdrive://</c> or <c>ssh://</c> panel simply does not appear.
/// </summary>
public static class PanelRefreshHelper
{
    /// <summary>
    /// Arranges for every panel showing the folder that contains one of <paramref name="affectedUrls"/>
    /// to be re-read once <paramref name="task"/> finishes.
    ///
    /// Subscribe <em>before</em> handing the task to the <see cref="TaskManager"/>: a small local
    /// copy is regularly over before <c>Add</c> returns, and a subscription made afterwards would
    /// miss the one status change the task ever raises.
    ///
    /// A run that failed (<see cref="BackgroundTask.Error"/>) is left alone: there is nothing new to
    /// show, and re-listing a remote folder costs a round trip. A cancelled one is not — a folder
    /// copy stopped half way through really did put files in the target.
    /// </summary>
    public static void RefreshWhenFinished(
        BackgroundTask task,
        IContentModelService? contentModelService,
        params Url?[] affectedUrls)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (contentModelService is null)
        {
            return;
        }

        var folders = new List<string>();
        foreach (var url in affectedUrls)
        {
            if (string.IsNullOrEmpty(url?.FullUrl))
            {
                continue;
            }

            string folder;
            try
            {
                folder = Normalize(url.ParentUrl);
            }
            catch
            {
                // Url.ParentUrl assumes a path well formed for the drive it belongs to and throws
                // on anything else. This runs on the key handler that pressed Enter in the dialog,
                // so a target the user mistyped has to cost the refresh, not the whole panel.
                continue;
            }

            if (folder.Length > 0 && !folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(folder);
            }
        }

        if (folders.Count == 0)
        {
            return;
        }

        void OnTaskChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BackgroundTask.Status)
                || task.Status == iConsole.Data.TaskStatus.Started)
            {
                return;
            }

            task.PropertyChanged -= OnTaskChanged;

            if (task.Error is not null)
            {
                return;
            }

            // Inline, on the TaskManager worker that just finished the operation. Re-listing a
            // remote folder is a network round trip, but that thread is exactly where that belongs:
            // it is off the render thread and off the key handler, which is all the content models
            // require — FilePanelRefreshTask writes into them from a worker too. Handing it to the
            // thread pool instead would be the one unreliable option here, because the app keeps
            // several pool threads parked forever in TaskManager's blocking worker loop; a queued
            // continuation can then wait seconds for the pool to grow.
            RefreshPanels(contentModelService, folders);
        }

        task.PropertyChanged += OnTaskChanged;
    }

    /// <summary>
    /// Re-reads every panel showing a local folder, for a change made with no task to watch (a date or an attribute,
    /// a metadata strip). A panel that cannot be re-read does not stop the others.
    /// </summary>
    public static void RefreshLocalPanels(IContentModelService? contentModelService)
    {
        foreach (var model in contentModelService?.ContentModels?.ToList() ?? [])
        {
            try
            {
                if (model.Url?.ContainerType == ContainerType.FileSystem)
                {
                    model.Refresh(forceRefresh: true);
                }
            }
            catch
            {
                // A panel that cannot be re-read must not keep the other from it.
            }
        }
    }

    /// <summary>Rebuilds the list of every panel narrowed by a label filter, after the labels changed; each keeps its cursor on its entry.</summary>
    public static void RefreshEntryFilters(IContentModelService? contentModelService)
    {
        foreach (var model in contentModelService?.ContentModels?.ToList() ?? [])
        {
            if (model is ContentModelBase { EntryFilterDescription: not null } filtered)
            {
                filtered.RefreshEntryFilter();
            }
        }
    }

    /// <summary>
    /// Like <see cref="RefreshWhenFinished"/>, but for operations that change the contents of whole
    /// folders (a synchronisation): the URLs are the folders themselves, not entries inside them.
    /// </summary>
    public static void RefreshFoldersWhenFinished(
        BackgroundTask task,
        IContentModelService? contentModelService,
        params Url?[] folders)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (contentModelService is null)
        {
            return;
        }

        var normalized = folders
            .Select(Normalize)
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count == 0)
        {
            return;
        }

        void OnTaskChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BackgroundTask.Status)
                || task.Status == iConsole.Data.TaskStatus.Started)
            {
                return;
            }

            task.PropertyChanged -= OnTaskChanged;

            // Unlike a failed copy, a synchronisation that failed part-way did change the folders.
            RefreshPanels(contentModelService, normalized);
        }

        task.PropertyChanged += OnTaskChanged;
    }

    private static void RefreshPanels(IContentModelService contentModelService, IReadOnlyList<string> folders)
    {
        foreach (var contentModel in contentModelService.ContentModels.AsSnapshot())
        {
            try
            {
                if (folders.Contains(Normalize(contentModel?.Url), StringComparer.OrdinalIgnoreCase))
                {
                    contentModel!.Refresh(forceRefresh: true);

                    // Every drive but the local one re-lists synchronously inside Refresh, so the
                    // listing here is already the new one and Update clamps the cursor against it —
                    // which is what makes deleting the last row leave the cursor somewhere real.
                    // ContentModelFileSystem only enqueues a FilePanelRefreshTask, and that task
                    // does its own Update once the enumeration lands; this one is then a harmless
                    // no-op against the listing that is still on screen.
                    contentModel.Update();
                    contentModel.ScrollToVisible();
                }
            }
            catch
            {
                // Best-effort: a panel that cannot be re-read — a dropped SSH connection, a device
                // unplugged mid-copy — must not stop the other panel from updating.
            }
        }
    }

    /// <summary>
    /// A folder URL is written both with and without its trailing separator — <see cref="Url.ParentUrl"/>
    /// drops it, a panel's own <see cref="IContentModel.Url"/> usually keeps it — so the two have to
    /// be compared with it off.
    /// </summary>
    private static string Normalize(Url? url) =>
        (url?.FullUrl ?? string.Empty).TrimEnd('/', '\\');
}

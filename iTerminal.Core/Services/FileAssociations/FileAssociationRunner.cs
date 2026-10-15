using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;

namespace iTerminal.Core.Services.FileAssociations;

/// <summary>
/// What the keys and the "Open with" dialog share: which program a file under the cursor opens
/// with, and starting it without holding up the key handler.
///
/// <para>A launch is only <em>started</em> here. A windowed program is on its own once it runs; a
/// terminal program keeps the screen for as long as the user stays in it. Either way the result has
/// nowhere to be shown by the time it is known — the dialog is closed, the key long handled — so a
/// program that could not be started is reported as a notification.</para>
/// </summary>
public class FileAssociationRunner(
    IFileAssociationService associations,
    IFileLauncher launcher,
    IContentModelService contentModelService,
    INotificationService? notificationService = null)
{
    public IFileAssociationService Associations => associations;

    /// <summary>The launch started last, including what follows it. Nothing waits on it; it exists so a test can.</summary>
    public Task LastRun { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// The path of <paramref name="url"/> when it is a file on the local disk, otherwise null. A
    /// program is handed a path, so a file inside an archive, a container or on an SSH host has
    /// nothing to hand over — the same limit the music viewer and the solution viewer have.
    /// </summary>
    public static string? LocalFilePath(Url? url)
    {
        if (url is null
            || url.ContainerType != ContainerType.FileSystem
            || url.PathType != PathType.File
            || string.IsNullOrEmpty(url.Path))
        {
            return null;
        }

        try
        {
            return File.Exists(url.Path) ? url.Path : null;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The program the user chose for the file at <paramref name="url"/>, or null: no choice made, or not a local file.</summary>
    public FileAssociation? DefaultFor(Url? url, FileAssociationKind kind) =>
        LocalFilePath(url) is { } path ? associations.GetDefault(Path.GetFileName(path), kind) : null;

    public Task Start(FileAssociation association, string filePath)
    {
        LastRun = Task.Run(async () =>
        {
            var outcome = await Guarded(() => launcher.LaunchAsync(association, filePath));
            Report(outcome, association.Name, filePath);

            if (association.InTerminal && outcome.Started)
            {
                // Whatever the program did to the folder is unknown, and only the local drive
                // notices its own changes — the same reason Ctrl+O re-reads the panels.
                foreach (var model in contentModelService.ContentModels.ToList())
                {
                    try
                    {
                        model.Refresh(forceRefresh: true);
                    }
                    catch
                    {
                        // A panel that cannot be re-read must not keep the other from it.
                    }
                }
            }
        });
        return LastRun;
    }

    public Task StartSystemDefault(string filePath)
    {
        LastRun = Task.Run(async () =>
            Report(await Guarded(() => launcher.OpenWithSystemDefaultAsync(filePath)), "The system default", filePath));
        return LastRun;
    }

    private static async Task<LaunchOutcome> Guarded(Func<Task<LaunchOutcome>> launch)
    {
        try
        {
            return await launch();
        }
        catch (Exception exception)
        {
            return LaunchOutcome.Failed(exception.Message);
        }
    }

    private void Report(LaunchOutcome outcome, string program, string filePath)
    {
        if (!outcome.Started)
        {
            notificationService?.Notify(
                $"Could not open {Path.GetFileName(filePath)}",
                outcome.Failure ?? $"{program} did not start.",
                NotificationSeverity.Error);
        }
    }
}

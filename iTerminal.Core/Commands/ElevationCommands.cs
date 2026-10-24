using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Screens;
using iConsole.Services;
using iTerminal.Core.Services.Elevation;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Shift+A: starts iTerminal again with administrator (Windows) or root (Unix) rights and quits
/// this copy. The attempt runs on a task of its own — a UAC prompt or a password prompt waits for a
/// person — and a refusal is a notification, with the app left as it was.
/// </summary>
public class RestartAsAdministratorCommand(
    IElevationService elevationService,
    INotificationService? notificationService = null) : Command
{
    public override string Title => "Restart as administrator";
    public override string Description => "Start iTerminal again with administrator (root) rights, and close this copy.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } = new Key(ConsoleKey.A, true, false, true);

    /// <summary>The attempt started last. Nothing waits on it; it exists so a test can.</summary>
    public Task AttemptTask { get; private set; } = Task.CompletedTask;

    public override bool CanExecute() => elevationService.IsSupported && !elevationService.IsElevated;

    public override Task ExecuteAsync(object? args)
    {
        var desktop = (Control?.GetRootControl() as Screen)?.Desktop;
        AttemptTask = Task.Run(async () =>
        {
            ElevationOutcome outcome;
            try
            {
                outcome = await elevationService.RestartElevatedAsync();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A platform that throws is a refusal the user is told about; uncaught, the attempt just ended without a word.
                outcome = new ElevationOutcome(ElevationResult.Failed, exception.Message);
            }

            if (outcome.Started)
            {
                // The elevated copy waits for this one to end (Windows) or replaces it (Unix).
                desktop?.CancellationTokenSource.Cancel();
                return;
            }

            notificationService?.Notify(
                "Not restarted as administrator",
                outcome.Message ?? "The request was not carried out.",
                outcome.Result == ElevationResult.Cancelled ? NotificationSeverity.Info : NotificationSeverity.Warning);
        });

        return Task.CompletedTask;
    }
}

/// <summary>
/// Ctrl+Shift+R: runs the program under the cursor with administrator (root) rights — Explorer's "Run
/// as administrator", or <c>sudo</c> on the file. Local programs only: the platform is handed a path.
/// </summary>
public class RunAsAdministratorCommand(
    IContentModelService contentModelService,
    IElevationService elevationService,
    INotificationService? notificationService = null) : Command
{
    public override string Title => "Run as administrator";
    public override string Description => "Run the program under the cursor with administrator (root) rights.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } = new Key(ConsoleKey.R, true, false, true);

    /// <summary>The attempt started last. Nothing waits on it; it exists so a test can.</summary>
    public Task AttemptTask { get; private set; } = Task.CompletedTask;

    private string? Target
    {
        get
        {
            var model = contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();
            return FocusedEntry.ActionableUrl(model) is { ContainerType: ContainerType.FileSystem, PathType: PathType.File } url
                && elevationService.CanRunElevated(url.FullName)
                    ? url.FullName
                    : null;
        }
    }

    public override bool CanExecute() => Target is not null;

    public override Task ExecuteAsync(object? args)
    {
        if (Target is not { } path)
        {
            return Task.CompletedTask;
        }

        AttemptTask = Task.Run(async () =>
        {
            ElevationOutcome outcome;
            try
            {
                outcome = await elevationService.RunElevatedAsync(path);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                outcome = new ElevationOutcome(ElevationResult.Failed, exception.Message);
            }

            if (!outcome.Started && outcome.Result != ElevationResult.Cancelled)
            {
                notificationService?.Notify(
                    "Could not run as administrator",
                    outcome.Message ?? Path.GetFileName(path),
                    NotificationSeverity.Warning);
            }

            // A program run in the terminal may have changed the folder; only the local drive watches it,
            // but a program started detached finishes on its own time, so this is only the quick case.
            foreach (var model in contentModelService.ContentModels.ToList())
            {
                model.Refresh(forceRefresh: true);
            }
        });

        return Task.CompletedTask;
    }
}

using iConsole.Localization;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services.Defender;

namespace iTerminal.Core.Commands;

/// <summary>
/// Scan with Microsoft Defender: hands the marked files and folders, or the one under the cursor, to
/// Defender's command-line client. Windows only, local disk only — the scanner is given a path. The
/// scan runs on a task of its own (a folder can take minutes) and the verdict is a notification.
/// </summary>
public class ScanWithDefenderCommand(
    IContentModelService contentModelService,
    IMalwareScanService scanService,
    INotificationService? notificationService = null) : Command
{
    public override string Title => "Scan with Microsoft Defender";
    public override string Description => "Scan the marked files and folders (or the one under the cursor) for malware with Microsoft Defender.";

    /// <summary>The scan started last. Nothing waits on it; it exists so a test can.</summary>
    public Task ScanTask { get; private set; } = Task.CompletedTask;

    private IReadOnlyList<string> Targets
    {
        get
        {
            var model = contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();
            var urls = FocusedEntry.ActionableUrls(model);
            return urls.Count > 0 && urls.All(url => url.ContainerType == ContainerType.FileSystem)
                ? [.. urls.Select(url => url.FullName)]
                : [];
        }
    }

    public override bool CanExecute() => scanService.IsAvailable && Targets.Count > 0;

    public override Task ExecuteAsync(object? args)
    {
        var targets = Targets;
        if (targets.Count == 0 || !scanService.IsAvailable)
        {
            return Task.CompletedTask;
        }

        var label = targets.Count == 1 ? Path.GetFileName(targets[0].TrimEnd('\\', '/')) : $"{targets.Count} items";
        notificationService?.Notify("Microsoft Defender", string.Format(UiText.Get("Notify.Scanning", "Scanning {0}…"), label), NotificationSeverity.Info);

        ScanTask = Task.Run(async () =>
        {
            int threats = 0;
            string? failure = null;
            foreach (var target in targets)
            {
                try
                {
                    var result = await scanService.ScanAsync(target);
                    if (result.Outcome == ScanOutcome.ThreatsFound)
                    {
                        threats++;
                    }
                    else if (result.Outcome == ScanOutcome.Failed)
                    {
                        failure ??= result.Message;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A scan that throws is a failed scan, and the rest still run. Uncaught, it ended the task with no word
                    // to the user after "Scanning…".
                    failure ??= exception.Message;
                }
            }

            if (threats > 0)
            {
                notificationService?.Notify("Microsoft Defender", string.Format(UiText.Get("Notify.ThreatsFound", "Threats found in {0} of {1} item(s). Open Windows Security for details."), threats, targets.Count), NotificationSeverity.Warning);
            }
            else if (failure is not null)
            {
                notificationService?.Notify(UiText.Get("Notify.DefenderFailed", "Microsoft Defender scan failed"), failure, NotificationSeverity.Warning);
            }
            else
            {
                notificationService?.Notify("Microsoft Defender", string.Format(UiText.Get("Notify.NoThreats", "No threats found in {0}."), label), NotificationSeverity.Info);
            }
        });

        return Task.CompletedTask;
    }
}

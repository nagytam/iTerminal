using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Docker;
using iTerminal.Core.Services.Preview;
using Serilog;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// A panel on the <c>docker://</c> drive: the containers at the root, a container's file system
/// below. All of the work is <see cref="IDockerDriveService"/>'s.
///
/// <para>Beyond the files, the panel is where a container is operated: Shift+F9 starts or stops the
/// container under the cursor and Ctrl+Shift+F9 restarts it (<see cref="RunContainerAction"/>), the
/// quick view shows its log (<see cref="BuildPreviewAsync"/>), and the terminal runs its commands
/// inside the container the panel is in — or the one under the cursor at the root.</para>
/// </summary>
public class ContentModelDockerDrive(
    string name,
    ISettingsService? settingsService,
    IDockerDriveService dockerDriveService,
    FolderHistoryService? folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    ILogger? logger = null)
    : ContentModelAsyncDrive(name, settingsService, folderHistoryService, frequentExecutionGuardService, logger),
      IEntryPreviewSource,
      ITerminalPromptSource,
      Services.Shell.IShellLaunchSource
{
    /// <summary>How much of a container's log the quick view shows.</summary>
    public const int LogLines = 200;

    protected override string Scheme => Container.DOCKER;

    public override ContainerType ContainerType => ContainerType.Docker;

    public override IFileSystem FileSystem => dockerDriveService;

    private bool IsContainerList => DockerDriveService.Split(Url).Container.Length == 0;

    /// <summary>The root lists containers; nothing can be created or copied into it.</summary>
    public override bool IsReadOnly => IsContainerList;

    /// <summary>Containers are not files: nothing on the root may be marked for F5 or F8.</summary>
    public override bool CanMark => !IsContainerList;

    protected override Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken) =>
        dockerDriveService.ListAsync(url, cancellationToken);

    protected override string DescribeLocation()
    {
        var (container, path) = DockerDriveService.Split(Url);
        return container.Length == 0 ? "docker: containers" : $"docker: {container}:/{path}";
    }

    public override string GetFooter()
    {
        // On the container list the keys that operate a container are named here: no row says them.
        // Short enough for a half-width panel at 140 columns — the border elides a longer one in the middle.
        var footer = base.GetFooter();
        return IsContainerList && StatusMessage.Length == 0
            ? $"{footer}│ Shift+F9: start/stop  Ctrl+Shift+F9: restart "
            : footer;
    }

    #region Starting and stopping

    /// <summary>The start, stop or restart started last. Nothing waits on it; it exists so a test can.</summary>
    public Task ContainerActionTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Starts, stops or restarts <paramref name="container"/> on a background task — <c>docker stop</c>
    /// alone waits up to ten seconds — and returns at once. The footer says what is happening, then
    /// what happened; the listing is read again so the row shows the new state.
    /// </summary>
    public void RunContainerAction(DockerContainerEntry container, DockerContainerAction action)
    {
        var (doing, done) = action switch
        {
            DockerContainerAction.Start => ("Starting", "started"),
            DockerContainerAction.Stop => ("Stopping", "stopped"),
            _ => ("Restarting", "restarted"),
        };

        var url = Url;
        StatusMessage = $"{doing} {container.ContainerName}…";
        Update();

        ContainerActionTask = Task.Run(async () =>
        {
            string outcome;
            try
            {
                await dockerDriveService.ControlAsync(container.ContainerName, action, CancellationToken.None);
                outcome = $"{container.ContainerName} {done}";
            }
            catch (Exception exception)
            {
                outcome = $"{container.ContainerName}: {exception.Message}";
            }

            if (!IsStill(url))
            {
                return;
            }

            // A listing that lands clears the footer's message, so the outcome is written after it.
            Refresh();
            await ListingTask;
            if (IsStill(url))
            {
                StatusMessage = outcome;
                Update();
            }
        });
    }

    private bool IsStill(Url url) => string.Equals(Url?.FullUrl, url.FullUrl, StringComparison.Ordinal);

    #endregion

    #region The log in the quick view

    /// <summary>
    /// A container row previews as its state and the end of its log, opened on the last line. Any
    /// other row — a file or folder inside a container — is previewed like a file (null).
    /// </summary>
    public async Task<FilePreview?> BuildPreviewAsync(IEntry entry, int width, CancellationToken cancellationToken)
    {
        if (entry is not DockerContainerEntry container)
        {
            return null;
        }

        List<string> lines =
        [
            string.Empty,
            FilePreviewService.Fit($" State:   {container.Status}", width),
            FilePreviewService.Fit($" Image:   {container.Image}", width),
        ];
        if (container.Modified is { } created)
        {
            lines.Add($" Created: {created:yyyy-MM-dd HH:mm}");
        }

        lines.Add(string.Empty);
        string summary;
        try
        {
            var log = await dockerDriveService.GetLogsAsync(container.ContainerName, LogLines, cancellationToken);
            lines.Add(FilePreviewService.Fit($" ── log: the last {LogLines} lines ──", width));
            lines.AddRange(log.Count == 0
                ? [" (nothing logged yet)"]
                : log.Select(line => FilePreviewService.Fit(" " + line, width)));
            summary = log.Count == 0 ? "no log output" : $"{log.Count:N0} log lines · Ctrl+R here: reload";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            lines.Add(FilePreviewService.Fit($" Cannot read the log: {exception.Message}", width));
            summary = "no log";
        }

        return new FilePreview($"{container.ContainerName} — log", summary, lines) { StartAtEnd = true };
    }

    #endregion

    #region The terminal

    /// <summary>
    /// The container a terminal command runs in and the folder it starts in: the panel's own, or at
    /// the container list the container under the cursor, in <c>/</c>. Null on the list with the
    /// cursor elsewhere.
    /// </summary>
    private (string Container, string Path)? TerminalTarget(Url? url)
    {
        var (container, path) = DockerDriveService.Split(url ?? new Url(Container.DOCKER));
        if (container.Length > 0)
        {
            return (container, DockerDriveService.ContainerPath(path));
        }

        return GetFocusedEntry() is DockerContainerEntry row ? (row.ContainerName, "/") : null;
    }

    public string? TerminalPrompt => TerminalTarget(Url) is { } target ? $"{target.Container}:{target.Path}" : null;

    /// <summary>Ctrl+O: an interactive shell inside the container — the same one the terminal's commands run in.</summary>
    public Services.Shell.ShellLaunch? GetShellLaunch(string? configuredShell) =>
        TerminalTarget(Url) is { } target ? Services.Shell.ShellLaunch.Docker(target.Container, target.Path) : null;

    /// <summary>
    /// Runs the terminal's command inside the container with <c>sh -c</c> — the ssh drive's bargain,
    /// one command at a time, not an interactive shell: the terminal has no TTY to hand one, and a
    /// command that waits for input gets a closed stdin rather than hanging.
    /// </summary>
    public override async Task RunTerminalCommandAsync(ITerminalModel terminalModel)
    {
        var command = terminalModel.Command;
        if (TerminalTarget(command.WorkingDirectoryUrl) is not { } target)
        {
            command.Error = "Open a container, or put the cursor on one, to run commands inside it.";
            return;
        }

        if (string.IsNullOrWhiteSpace(command.Command))
        {
            return;
        }

        command.Host = target.Container;
        command.Username = string.Empty;
        command.StartTime = DateTime.Now;
        var result = await dockerDriveService.ExecAsync(target.Container, target.Path, command.Command, CancellationToken.None);
        command.ExitTime = DateTime.Now;
        command.ExitCode = result.ExitCode;
        command.Output = result.StandardOutput;
        command.Error = DockerDriveService.DescribeDockerProblem(result) ?? result.StandardError;
    }

    #endregion
}

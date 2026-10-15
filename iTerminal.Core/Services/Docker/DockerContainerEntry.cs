using iConsole.Data;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Services.Docker;

/// <summary>What can be done to a container from its row.</summary>
public enum DockerContainerAction
{
    Start,
    Stop,
    Restart,
}

/// <summary>
/// A container, as a row at the root of <c>docker://</c>. Enter opens its file system; Shift+F9 starts
/// or stops it, Ctrl+Shift+F9 restarts it, and the quick view shows its log. It is not a folder to
/// copy or delete (<see cref="IDriveObjectEntry"/>).
/// </summary>
public sealed record DockerContainerEntry : VirtualDriveEntry, IDriveObjectEntry
{
    public DockerContainerEntry(string containerName, string state, string status, string image, DateTime? created)
        : base(new Url(Container.DOCKER + containerName + "/"), containerName, DockerDriveService.ShortState(state), created)
    {
        ContainerName = containerName;
        State = state;
        Status = status;
        Image = image;
        Detail = $"{image} — {status}";
    }

    /// <summary>The name the CLI addresses the container by.</summary>
    public string ContainerName { get; }

    /// <summary>Docker's own state word, lower case: running, exited, paused, created, restarting, dead.</summary>
    public string State { get; }

    /// <summary>Docker's sentence for it: "Up 3 hours", "Exited (0) 2 days ago".</summary>
    public string Status { get; }

    public string Image { get; }

    /// <summary>Whether the container has processes that stopping it would end — what Shift+F9 decides by.</summary>
    public bool IsRunning => State is "running" or "paused" or "restarting";
}

using iTerminal.Core.Model.Workspaces;

namespace iTerminal.Core.Services.Workspaces;

/// <summary>Reads the screen's layout and puts it back: the one place that knows about panels and bars.</summary>
public interface IWorkspaceLayoutService
{
    /// <summary>The layout on screen now.</summary>
    WorkspaceLayout Capture();

    /// <summary>
    /// Puts <paramref name="layout"/> on screen. A part that cannot be restored (a folder that is gone)
    /// is left as it is and named in the result, which is empty when everything went back.
    /// </summary>
    Task<IReadOnlyList<string>> ApplyAsync(WorkspaceLayout layout);
}

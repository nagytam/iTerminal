using iTerminal.Core.Model.Workspaces;

namespace iTerminal.Core.Services.Workspaces;

/// <summary>
/// Named screen layouts: save the one on screen, switch back to any of them later. They are kept in
/// the order they were saved, which is also the order of the 1-9 quick keys in the manager.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>The longest name a workspace can have.</summary>
    public const int MaxNameLength = 40;

    IReadOnlyList<Workspace> GetAll();

    /// <summary>The workspace last saved or switched to; null before the first one, or after it was deleted.</summary>
    string? ActiveName { get; }

    /// <summary>The layout on screen now.</summary>
    WorkspaceLayout CaptureCurrent();

    /// <summary>Whether the screen still shows what <paramref name="workspace"/> saved.</summary>
    bool IsCurrent(Workspace workspace);

    /// <summary>Why <paramref name="name"/> cannot be used, or null. <paramref name="renaming"/> is the workspace being renamed, which may keep its own name.</summary>
    string? ValidateName(string name, string? renaming = null);

    /// <summary>The name a new workspace would get: "Workspace 1", then 2, ... whichever is free.</summary>
    string SuggestName();

    /// <summary>Saves the layout on screen under <paramref name="name"/>, replacing a workspace of that name in place.</summary>
    Workspace Save(string name);

    void Rename(string name, string newName);

    void Delete(string name);

    /// <summary>
    /// Puts the workspace on screen. Returns what could not be restored, one line each; empty when all of it
    /// went back. A workspace that does not exist is a problem line too.
    /// </summary>
    Task<IReadOnlyList<string>> RestoreAsync(string name);
}

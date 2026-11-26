using iTerminal.DevelopmentStudio.Model;

namespace iTerminal.DevelopmentStudio.Services;

/// <summary>Finds the solutions, .NET projects, Python scripts and PowerShell scripts under a folder.</summary>
public interface IDevelopmentWorkspaceScanner
{
    /// <summary>
    /// The items under <paramref name="folder"/>, solutions first with their projects listed after them, then the other
    /// projects and the scripts, each group in path order. Folders such as <c>bin</c>, <c>obj</c>, <c>.git</c> and
    /// virtual environments are skipped. Returns nothing when the folder does not exist.
    /// </summary>
    IReadOnlyList<DevelopmentProject> Scan(string folder);
}

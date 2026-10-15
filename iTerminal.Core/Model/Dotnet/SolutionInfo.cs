namespace iTerminal.Core.Model.Dotnet;

/// <summary>
/// A parsed .NET solution (.slnx or legacy .sln) and the projects it contains.
/// </summary>
/// <param name="Path">Absolute path to the solution file.</param>
/// <param name="Name">Solution name (file name without extension).</param>
/// <param name="Directory">Absolute directory containing the solution file (the dotnet working directory).</param>
/// <param name="Projects">Projects listed in the solution, in file order.</param>
public sealed record SolutionInfo(
    string Path,
    string Name,
    string Directory,
    IReadOnlyList<SolutionProject> Projects)
{
    public static SolutionInfo Empty { get; } =
        new(string.Empty, string.Empty, string.Empty, []);
}

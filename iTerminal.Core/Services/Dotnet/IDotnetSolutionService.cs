using iTerminal.Core.Model.Dotnet;

namespace iTerminal.Core.Services.Dotnet;

/// <summary>
/// Parses .NET solution files and runs <c>dotnet</c> CLI commands (build / test / run)
/// against a solution or one of its projects.
/// </summary>
public interface IDotnetSolutionService
{
    /// <summary>
    /// Parses a solution file. Supports the modern XML format (<c>.slnx</c>) and the
    /// legacy text format (<c>.sln</c>). Project paths are resolved to absolute paths.
    /// </summary>
    SolutionInfo ParseSolution(string solutionPath);

    /// <summary>
    /// The .NET SDKs installed on this machine, newest first. Empty when <c>dotnet</c> cannot be run.
    /// </summary>
    IReadOnlyList<DotnetSdk> ListSdks();

    /// <summary>
    /// Runs <paramref name="dotnetPath"/> (the <c>dotnet</c> host of the SDK to use) with the given arguments in
    /// <paramref name="workingDirectory"/>, streaming each line of stdout and stderr to <paramref name="onOutputLine"/> as it
    /// is produced. Returns the process exit code, or a negative value if it could not run or was cancelled.
    /// </summary>
    Task<int> RunAsync(
        string workingDirectory,
        string dotnetPath,
        IReadOnlyList<string> arguments,
        Action<string> onOutputLine,
        CancellationToken cancellationToken = default);
}

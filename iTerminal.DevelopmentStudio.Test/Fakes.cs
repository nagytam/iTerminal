using iTerminal.Core.Model.Dotnet;
using iTerminal.Core.Services.Dotnet;
using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Test;

/// <summary>Records every command and replays scripted output and an exit code.</summary>
public sealed class FakeProcessRunner : IDevelopmentProcessRunner
{
    public List<ProcessCommand> Commands { get; } = [];

    public List<string> Output { get; set; } = [];

    public int ExitCode { get; set; }

    public Task<int> RunAsync(ProcessCommand command, Action<string> onOutputLine, CancellationToken cancellationToken = default)
    {
        lock (Commands) Commands.Add(command);
        foreach (string line in Output) onOutputLine(line);
        return Task.FromResult(ExitCode);
    }
}

/// <summary>Returns the items it was given, whatever the folder.</summary>
public sealed class FakeWorkspaceScanner(IReadOnlyList<DevelopmentProject> items) : IDevelopmentWorkspaceScanner
{
    public IReadOnlyList<DevelopmentProject> Scan(string folder) => items;
}

/// <summary>The .NET CLI service the studio uses for SDKs, solutions and SBOMs.</summary>
public sealed class FakeDotnetSolutionService : IDotnetSolutionService
{
    public IReadOnlyList<DotnetSdk> Sdks { get; set; } = [];

    public SolutionInfo Solution { get; set; } = SolutionInfo.Empty;

    public List<SbomFormat> SbomRequests { get; } = [];

    public SbomResult Sbom { get; set; } = new("/src/App.cdx.xml", 3, 0);

    public IReadOnlyList<DotnetSdk> ListSdks() => Sdks;

    public SolutionInfo ParseSolution(string solutionPath) => Solution;

    public SbomResult GenerateSbom(SolutionInfo solution, SbomFormat format)
    {
        lock (SbomRequests) SbomRequests.Add(format);
        return Sbom;
    }

    public Task<int> RunAsync(string workingDirectory, string dotnetPath, IReadOnlyList<string> arguments, Action<string> onOutputLine, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}

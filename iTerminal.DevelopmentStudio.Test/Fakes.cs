using iTerminal.Core.Model.Dotnet;
using iTerminal.Core.Services.Dotnet;
using iTerminal.Core.Services.Sdk;
using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Test;

/// <summary>Records every command and replays scripted output and an exit code.</summary>
public sealed class FakeProcessRunner : IDevelopmentProcessRunner
{
    public List<ProcessCommand> Commands { get; } = [];

    public List<string> Output { get; set; } = [];

    public int ExitCode { get; set; }

    /// <summary>Runs for each command before its output is replayed (a test writes the files a real tool would).</summary>
    public Action<ProcessCommand>? OnRun { get; set; }

    /// <summary>The output of one command, when set; otherwise <see cref="Output"/>.</summary>
    public Func<ProcessCommand, IReadOnlyList<string>>? OutputFor { get; set; }

    public Task<int> RunAsync(ProcessCommand command, Action<string> onOutputLine, CancellationToken cancellationToken = default)
    {
        lock (Commands) Commands.Add(command);
        OnRun?.Invoke(command);
        foreach (string line in OutputFor?.Invoke(command) ?? Output) onOutputLine(line);
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

/// <summary>An SDK manager with nothing to download and no install: the lists and the JDK choice are what a test sets.</summary>
public sealed class FakeSdkManager : ISdkManager
{
    public IReadOnlyList<DotnetSdk> DotNet { get; set; } = [];

    public IReadOnlyList<InstalledJdk> Jdks { get; set; } = [];

    public IReadOnlyList<AvailableSdk> Available { get; set; } = [];

    public string? ActiveJavaHome { get; private set; }

    public List<AvailableSdk> Installs { get; } = [];

    public IReadOnlyList<DotnetSdk> InstalledDotNetSdks() => DotNet;

    public IReadOnlyList<InstalledJdk> InstalledJdks() => Jdks;

    public void UseJavaHome(string? home) => ActiveJavaHome = home;

    public Task<IReadOnlyList<AvailableSdk>> ListAvailableAsync(SdkKind kind, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AvailableSdk>>(Available.Where(sdk => sdk.Kind == kind).ToList());

    public Task<string> InstallAsync(AvailableSdk sdk, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        lock (Installs) Installs.Add(sdk);
        return Task.FromResult(sdk.Version);
    }

    public List<string> Removed { get; } = [];

    public void Uninstall(string versionFolder)
    {
        lock (Removed) Removed.Add(versionFolder);
    }
}

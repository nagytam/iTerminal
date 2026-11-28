using System.Text.Json.Nodes;
using iTerminal.DevelopmentStudio.Model;

namespace iTerminal.DevelopmentStudio.Services;

/// <summary>How to start the debug adapter for a language: the adapter speaks the Debug Adapter Protocol on its standard input and output.</summary>
/// <param name="FileName">The adapter executable.</param>
/// <param name="Arguments">Its arguments.</param>
/// <param name="Launch">The <c>launch</c> request's arguments: what the adapter starts and how.</param>
public sealed record DebugPlan(string FileName, IReadOnlyList<string> Arguments, JsonObject Launch);

/// <summary>The commands for one language. A null command means the language does not offer that action.</summary>
public interface IDevelopmentToolchain
{
    DevelopmentLanguage Language { get; }

    /// <summary>Builds the item (a syntax check for scripts).</summary>
    ProcessCommand? Build(DevelopmentProject project);

    /// <summary>Runs the item without a debugger.</summary>
    ProcessCommand? Run(DevelopmentProject project);

    /// <summary>Runs the item's tests.</summary>
    ProcessCommand? Test(DevelopmentProject project);

    /// <summary>
    /// Lists the item's tests without running them, after a build (see <see cref="TestDiscoveryParser"/>). Null when the language
    /// has no discovery here.
    /// </summary>
    ProcessCommand? ListTests(DevelopmentProject project) => null;

    /// <summary>Removes what a build leaves behind: <c>dotnet clean</c>, or the cache folders of a script.</summary>
    ProcessCommand? Clean(DevelopmentProject project);

    /// <summary>The folders a clean deletes when the language has no clean command (empty otherwise).</summary>
    IReadOnlyList<string> CacheFolders(DevelopmentProject project);

    /// <summary>
    /// The plan to debug the item. For .NET, <paramref name="builtAssembly"/> is the built DLL (see <see cref="FindBuiltAssembly"/>).
    /// Null when the language has no debugger here.
    /// </summary>
    DebugPlan? PlanDebug(DevelopmentProject project, string? builtAssembly, bool debugTests);

    /// <summary>The DLL a build of a .NET project leaves in <c>bin/Debug</c>, the newest one; null when there is none.</summary>
    string? FindBuiltAssembly(DevelopmentProject project) => null;

    /// <summary>
    /// The commands that measure the coverage of the item, in order. Each writes into <paramref name="resultsFolder"/>, and
    /// the last one leaves a Cobertura report there (see <see cref="FindCoverageReport"/>). Empty when the language has none here.
    /// </summary>
    IReadOnlyList<ProcessCommand> Coverage(DevelopmentProject project, string resultsFolder) => [];

    /// <summary>
    /// The command that analyses the item's code. Its findings come out as build diagnostics (file, line, column and a
    /// severity), so the Analysis tab reads them the way the Error List reads a build's. Null when the language has no analyzer here.
    /// </summary>
    ProcessCommand? Analyze(DevelopmentProject project) => null;

    /// <summary>The Cobertura report a coverage run left under <paramref name="resultsFolder"/>, or null when there is none.</summary>
    string? FindCoverageReport(string resultsFolder)
    {
        if (!Directory.Exists(resultsFolder)) return null;
        return Directory.EnumerateFiles(resultsFolder, "*.xml", SearchOption.AllDirectories)
            .FirstOrDefault(file => Path.GetFileName(file).Equals("coverage.cobertura.xml", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals("coverage.xml", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The command that profiles the item into <paramref name="profilePath"/>. Null when the language offers no profiler here.</summary>
    ProcessCommand? Profile(DevelopmentProject project, string profilePath) => null;

    /// <summary>The command that prints the profile at <paramref name="profilePath"/> as tab-separated lines (see <c>PythonProfileParser</c>).</summary>
    ProcessCommand? ProfileSummary(DevelopmentProject project, string profilePath) => null;
}

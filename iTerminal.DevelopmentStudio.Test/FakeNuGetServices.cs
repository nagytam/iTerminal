using iConsole.Prompts;
using iConsole.Services;
using iTerminal.DevelopmentStudio.Model.NuGet;
using iTerminal.DevelopmentStudio.Services.NuGet;

namespace iTerminal.DevelopmentStudio.Test;

/// <summary>Returns a scripted listing and records every add and remove, as "add Project Id Version" and "remove Project Id".</summary>
public sealed class FakeNuGetPackageService : INuGetPackageService
{
    private readonly object _gate = new();
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Projects { get; set; } = [];

    public NuGetListing Listing { get; set; } = new([], [], Succeeded: true);

    /// <summary>The exit code every add and remove returns.</summary>
    public int ExitCode { get; set; }

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate) return [.. _calls];
        }
    }

    public IReadOnlyList<string> ProjectsOf(string targetPath) => Projects;

    public Task<NuGetListing> ListAsync(string targetPath, string dotnetHost, CancellationToken cancellationToken = default) =>
        Task.FromResult(Listing);

    public Task<int> AddAsync(string projectPath, string packageId, string? version, string dotnetHost, Action<string> onOutputLine, CancellationToken cancellationToken = default)
    {
        Record($"add {Name(projectPath)} {packageId} {version ?? "latest"}");
        onOutputLine($"PackageReference for package '{packageId}' updated in file '{projectPath}'.");
        return Task.FromResult(ExitCode);
    }

    public Task<int> RemoveAsync(string projectPath, string packageId, string dotnetHost, Action<string> onOutputLine, CancellationToken cancellationToken = default)
    {
        Record($"remove {Name(projectPath)} {packageId}");
        onOutputLine($"Removing PackageReference for package '{packageId}' from project '{projectPath}'.");
        return Task.FromResult(ExitCode);
    }

    private void Record(string call)
    {
        lock (_gate) _calls.Add(call);
    }

    private static string Name(string path) => Path.GetFileNameWithoutExtension(path);
}

/// <summary>Answers every text prompt with <see cref="Answer"/> (null for a cancelled one) and records the questions asked.</summary>
public sealed class FakePromptService : IPromptService
{
    public string? Answer { get; set; }

    public List<string> Questions { get; } = [];

    public Task<T?> AskAsync<T>(TextPrompt<T> prompt, CancellationToken cancellationToken = default)
    {
        Questions.Add(prompt.Question);
        return Task.FromResult((T?)(object?)Answer);
    }

    public Task<T?> SelectAsync<T>(SelectionPrompt<T> prompt, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<T>?> MultiSelectAsync<T>(MultiSelectionPrompt<T> prompt, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool?> ConfirmAsync(ConfirmationPrompt prompt, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

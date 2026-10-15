using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Test.Services.RuntimeDependencies;

/// <summary>A dependency whose disk, network and install are plain fields the test sets.</summary>
internal sealed class FakeRuntimeDependency(string expectedVersion, string id = "fake") : IRuntimeDependency
{
    private readonly Version _expected = Version.Parse(expectedVersion);

    public string Id => id;
    public string Name => "Fake";
    public string Purpose => "A stand-in for a real component";
    public string ExpectedVersion => expectedVersion;

    public string? Installed { get; set; }
    public IReadOnlyList<string> Published { get; set; } = [];
    public Exception? ListFailure { get; set; }
    public Exception? InstallFailure { get; set; }
    public List<string> Installs { get; } = [];
    public int Activations { get; private set; }
    public int Invalidations { get; private set; }

    public bool IsCompatible(Version version) => version.Major == _expected.Major && version.Minor == _expected.Minor;

    public string? InstalledVersion() => Installed;

    public Task<IReadOnlyList<string>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        if (ListFailure is not null)
        {
            throw ListFailure;
        }

        return Task.FromResult(Published);
    }

    public Task InstallAsync(string version, IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report($"fetching {version}");
        if (InstallFailure is not null)
        {
            throw InstallFailure;
        }

        Installs.Add(version);
        Installed = version;
        return Task.CompletedTask;
    }

    public void Activate() => Activations++;

    public void Invalidate()
    {
        Invalidations++;
        Installed = null;
    }
}

using iConsole.Localization;
namespace iTerminal.Core.Services.RuntimeDependencies;

/// <summary>
/// The Whisper speech model the AI prompt bar's voice input runs offline. The engine (whisper.cpp through
/// Whisper.net) ships with the application, a few megabytes per platform; the model is 148 MB and is downloaded
/// into the data folder by the setup dialog, like Ollama's.
/// </summary>
/// <remarks>
/// <para>Layout: <c>&lt;root&gt;/&lt;pack version&gt;/ggml-base.bin</c>. The version is the version of the model
/// <em>pack</em>, not of a program: it is "1.0.0" for ggml-base. Nothing is published to look up, so a different
/// model later means a new pack version here (a new folder, a bumped <see cref="Built"/> major), which an
/// install of the old pack does not satisfy.</para>
/// <para>The file is fetched from a pinned commit of the <c>ggerganov/whisper.cpp</c> Hugging Face repository
/// and accepted only with the pinned size and SHA-256, so a moved branch or a swapped file cannot be installed.</para>
/// </remarks>
public sealed class WhisperRuntimeDependency : IRuntimeDependency
{
    public const string DependencyId = "whisper";

    public const string ModelFileName = "ggml-base.bin";

    /// <summary>The model pack this build is written for; an update stays on its major line.</summary>
    internal static readonly Version Built = new(1, 0, 0);

    internal const string ModelAddress =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/" + ModelFileName;

    internal const long ModelSize = 147951465;
    internal const string ModelSha256 = "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe";

    private readonly string _root;
    private readonly HttpClient _httpClient;
    private readonly Version _expected;
    private readonly string _address;
    private readonly long _size;
    private readonly string _sha256;

    public WhisperRuntimeDependency(string rootDirectory)
        : this(rootDirectory, new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, Built, ModelAddress, ModelSize, ModelSha256)
    {
    }

    internal WhisperRuntimeDependency(string rootDirectory, HttpClient httpClient, Version expected, string address, long size, string sha256)
    {
        _root = rootDirectory;
        _httpClient = httpClient;
        _expected = expected;
        _address = address;
        _size = size;
        _sha256 = sha256;
    }

    public string Id => DependencyId;

    public string Name => "Whisper";

    public string Purpose => UiText.Get("Dep.WhisperPurpose", "Offline speech model (base, 148 MB) for voice input in the AI bar");

    public string ExpectedVersion => _expected.ToString(3);

    public bool IsCompatible(Version version) => version.Major == _expected.Major;

    /// <summary>
    /// The model file of the installed pack, or null. The size is checked, not the hash: the file only gets its
    /// name after the hash matched, and hashing 148 MB must not happen where this is called from.
    /// </summary>
    public string? ModelPath()
    {
        string path = ModelPathFor(ExpectedVersion);
        try
        {
            return File.Exists(path) && new FileInfo(path).Length == _size ? path : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public string? InstalledVersion() => ModelPath() is null ? null : ExpectedVersion;

    /// <summary>Nothing is published to look up; the pack version is the one this build carries.</summary>
    public Task<IReadOnlyList<string>> ListVersionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public async Task InstallAsync(string version, IProgress<string> progress, CancellationToken cancellationToken)
    {
        Version parsed = Version.Parse(version);
        if (!IsCompatible(parsed))
        {
            throw new InvalidOperationException($"Whisper model pack {version} is not one this build can use.");
        }

        string target = ModelPathFor(parsed.ToString(3));
        await VerifiedDownload.DownloadAsync(_httpClient, _address, target, _size, _sha256, progress, cancellationToken);
        progress.Report(string.Format(UiText.Get("Dep.WhisperReady", "Whisper model {0} is ready"), parsed.ToString(3)));
    }

    /// <summary>Nothing is process-wide: the model path is read when a recording is transcribed.</summary>
    public void Activate()
    {
    }

    public void Invalidate()
    {
        try
        {
            File.Delete(ModelPathFor(ExpectedVersion));
        }
        catch (IOException)
        {
        }
    }

    private string ModelPathFor(string version) => Path.Combine(_root, version, ModelFileName);
}

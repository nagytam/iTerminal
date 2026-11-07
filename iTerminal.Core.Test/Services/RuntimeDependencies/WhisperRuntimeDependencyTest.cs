using System.Net;
using System.Security.Cryptography;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Test.Services.RuntimeDependencies;

[TestClass]
public class WhisperRuntimeDependencyTest
{
    private static readonly byte[] Model = "pretend this is a speech model"u8.ToArray();
    private static readonly string ModelHash = Convert.ToHexString(SHA256.HashData(Model)).ToLowerInvariant();

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(respond(request));
        }
    }

    private string _root = null!;

    [TestInitialize]
    public void CreateRoot() => _root = Path.Combine(Path.GetTempPath(), "whisper-test-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void DeleteRoot()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private WhisperRuntimeDependency Create(byte[] served, Version? expected = null) =>
        new(_root, new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(served) })),
            expected ?? new Version(1, 0, 0), "https://example.test/ggml-base.bin", Model.Length, ModelHash);

    private string ModelFile(string version = "1.0.0") => Path.Combine(_root, version, WhisperRuntimeDependency.ModelFileName);

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutTheFile_NothingIsInstalled()
    {
        WhisperRuntimeDependency dependency = Create(Model);

        Assert.IsNull(dependency.InstalledVersion());
        Assert.IsNull(dependency.ModelPath());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APartialDownload_IsNotAnInstall()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelFile())!);
        File.WriteAllBytes(ModelFile() + ".partial", Model);

        Assert.IsNull(Create(Model).InstalledVersion());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFileOfTheWrongSize_IsNotAnInstall()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelFile())!);
        File.WriteAllBytes(ModelFile(), Model[..^1]);

        Assert.IsNull(Create(Model).InstalledVersion());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Install_WritesTheVerifiedFile_AndReportsIt()
    {
        WhisperRuntimeDependency dependency = Create(Model);
        var messages = new List<string>();

        await dependency.InstallAsync("1.0.0", new Progress<string>(messages.Add), CancellationToken.None);

        Assert.AreEqual("1.0.0", dependency.InstalledVersion());
        CollectionAssert.AreEqual(Model, File.ReadAllBytes(dependency.ModelPath()!));
        Assert.IsFalse(File.Exists(ModelFile() + ".partial"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADownloadWithAnotherChecksum_IsDeleted_AndNothingIsInstalled()
    {
        byte[] tampered = (byte[])Model.Clone();
        tampered[0] ^= 0xFF;
        WhisperRuntimeDependency dependency = Create(tampered);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            dependency.InstallAsync("1.0.0", new Progress<string>(), CancellationToken.None));

        Assert.IsFalse(File.Exists(ModelFile()));
        Assert.IsFalse(File.Exists(ModelFile() + ".partial"));
        Assert.IsNull(dependency.InstalledVersion());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADownloadOfTheWrongSize_IsDeleted()
    {
        WhisperRuntimeDependency dependency = Create(Model[..^3]);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            dependency.InstallAsync("1.0.0", new Progress<string>(), CancellationToken.None));

        Assert.IsFalse(File.Exists(ModelFile() + ".partial"));
        Assert.IsNull(dependency.InstalledVersion());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedRequest_LeavesNothingBehind()
    {
        var dependency = new WhisperRuntimeDependency(
            _root,
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))),
            new Version(1, 0, 0),
            "https://example.test/ggml-base.bin",
            Model.Length,
            ModelHash);

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            dependency.InstallAsync("1.0.0", new Progress<string>(), CancellationToken.None));

        Assert.IsNull(dependency.InstalledVersion());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnOlderModelPack_DoesNotSatisfyANewerBuild()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelFile("1.0.0"))!);
        File.WriteAllBytes(ModelFile("1.0.0"), Model);
        WhisperRuntimeDependency newer = Create(Model, new Version(2, 0, 0));

        Assert.IsNull(newer.InstalledVersion());
        Assert.IsFalse(newer.IsCompatible(new Version(1, 0, 0)));
        Assert.IsTrue(newer.IsCompatible(new Version(2, 3, 1)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APackOfAnotherMajor_IsRefused()
    {
        WhisperRuntimeDependency dependency = Create(Model);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            dependency.InstallAsync("2.0.0", new Progress<string>(), CancellationToken.None));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Invalidate_MakesTheModelMissingAgain()
    {
        WhisperRuntimeDependency dependency = Create(Model);
        await dependency.InstallAsync("1.0.0", new Progress<string>(), CancellationToken.None);

        dependency.Invalidate();

        Assert.IsNull(dependency.InstalledVersion());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NothingIsPublishedToLookUp()
    {
        IReadOnlyList<string> versions = await Create(Model).ListVersionsAsync(CancellationToken.None);

        Assert.AreEqual(0, versions.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheShippedPinIsACommitAndASha256_NotABranch()
    {
        StringAssert.Contains(WhisperRuntimeDependency.ModelAddress, "/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/");
        Assert.IsFalse(WhisperRuntimeDependency.ModelAddress.Contains("/resolve/main/"));
        Assert.AreEqual(64, WhisperRuntimeDependency.ModelSha256.Length);
        Assert.AreEqual(147951465L, WhisperRuntimeDependency.ModelSize);
        Assert.AreEqual("1.0.0", new WhisperRuntimeDependency(_root).ExpectedVersion);
    }
}

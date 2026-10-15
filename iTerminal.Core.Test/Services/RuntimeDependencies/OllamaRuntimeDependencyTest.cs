using iTerminal.Core.Test.Helper;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using iTerminal.Core.AI;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Test.Services.RuntimeDependencies;

[TestClass]
public class OllamaRuntimeDependencyTest
{
    private const string Model = "qwen3.5:0.8b";
    private const string Api = "https://api.github.com/";
    private const string Downloads = "https://downloads.test/";
    private static readonly Version Built = new(0, 40, 4);

    private static readonly List<string> Roots = [];

    private static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"iTerminal.Ollama.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        lock (Roots)
        {
            Roots.Add(root);
        }

        return root;
    }

    [ClassCleanup]
    public static void DeleteRoots()
    {
        lock (Roots)
        {
            foreach (string root in Roots)
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    /// <summary>Records each line where it is reported, so the test reads it without waiting for a synchronisation context.</summary>
    private sealed class RecordingProgress : IProgress<string>
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_lines)
                {
                    return _lines.ToList();
                }
            }
        }

        public void Report(string value)
        {
            lock (_lines)
            {
                _lines.Add(value);
            }
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static OllamaRuntimeDependency NewDependency(string root, HttpMessageHandler handler) =>
        new(root, Model, new OllamaServer(new Uri("http://localhost:11434/"), new HttpClient(handler)),
            new GitHubReleaseFeed(new HttpClient(handler), Api), Built);

    /// <summary>A version folder as a finished install leaves it: the program, with the marker only when the model was downloaded.</summary>
    private static void MakeInstalled(string root, string version)
    {
        string binary = Path.Combine(root, version, "bin", "ollama");
        Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
        File.WriteAllText(binary, "program");
    }

    [TestMethod]
    public void AssetName_PicksTheArchiveOfThePlatform()
    {
        Assert.AreEqual("ollama-windows-amd64.zip", OllamaRuntimeDependency.AssetName(windows: true, macOS: false, Architecture.X64));
        Assert.AreEqual("ollama-windows-arm64.zip", OllamaRuntimeDependency.AssetName(windows: true, macOS: false, Architecture.Arm64));
        Assert.AreEqual("ollama-linux-amd64.tar.zst", OllamaRuntimeDependency.AssetName(windows: false, macOS: false, Architecture.X64));
        Assert.AreEqual("ollama-linux-arm64.tar.zst", OllamaRuntimeDependency.AssetName(windows: false, macOS: false, Architecture.Arm64));
        Assert.AreEqual("ollama-darwin.tgz", OllamaRuntimeDependency.AssetName(windows: false, macOS: true, Architecture.Arm64));
        Assert.IsNull(OllamaRuntimeDependency.AssetName(windows: false, macOS: false, Architecture.X86));
    }

    [TestMethod]
    public void InstalledVersion_IsNullUntilTheModelIsDownloaded()
    {
        string root = NewRoot();
        MakeInstalled(root, "0.40.4");
        var dependency = NewDependency(root, new StubHandler(_ => throw new InvalidOperationException("no network in this test")));

        Assert.IsNull(dependency.InstalledVersion(), "A program without its model is not a complete install.");

        File.WriteAllText(Path.Combine(root, OllamaRuntimeDependency.ModelMarker), Model);
        Assert.AreEqual("0.40.4", dependency.InstalledVersion());
    }

    [TestMethod]
    public void InstalledVersion_IsNullWhenTheModelMarkerNamesAnotherModel()
    {
        string root = NewRoot();
        MakeInstalled(root, "0.40.4");
        File.WriteAllText(Path.Combine(root, OllamaRuntimeDependency.ModelMarker), "llama3.2:1b");
        var dependency = NewDependency(root, new StubHandler(_ => throw new InvalidOperationException("no network in this test")));

        Assert.IsNull(dependency.InstalledVersion());
    }

    [TestMethod]
    public void Invalidate_ForgetsTheModelDownload()
    {
        string root = NewRoot();
        MakeInstalled(root, "0.40.4");
        File.WriteAllText(Path.Combine(root, OllamaRuntimeDependency.ModelMarker), Model);
        var dependency = NewDependency(root, new StubHandler(_ => throw new InvalidOperationException("no network in this test")));

        dependency.Invalidate();

        Assert.IsNull(dependency.InstalledVersion());
    }

    [TestMethod]
    public void IsCompatible_StaysOnTheBuildsMinorLine()
    {
        var dependency = NewDependency(NewRoot(), new StubHandler(_ => throw new InvalidOperationException("unused")));

        Assert.AreEqual("0.40.4", dependency.ExpectedVersion);
        Assert.IsTrue(dependency.IsCompatible(new Version(0, 40, 9)));
        Assert.IsFalse(dependency.IsCompatible(new Version(0, 41, 0)));
    }

    [TestMethod]
    public void Extract_UnpacksAZipAndRefusesAnEntryOutsideTheFolder()
    {
        string root = NewRoot();
        string package = Path.Combine(root, "ollama.zip");
        using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            WriteZipEntry(archive, "ollama.exe", "program");
            WriteZipEntry(archive, "lib/ollama/cuda.dll", "library");
            WriteZipEntry(archive, "../escaped.txt", "outside");
        }

        string destination = Path.Combine(root, "out");
        OllamaRuntimeDependency.Extract(package, destination);

        Assert.AreEqual("program", File.ReadAllText(Path.Combine(destination, "ollama.exe")));
        Assert.AreEqual("library", File.ReadAllText(Path.Combine(destination, "lib", "ollama", "cuda.dll")));
        Assert.IsFalse(File.Exists(Path.Combine(root, "escaped.txt")));
    }

    [TestMethod]
    public void Extract_UnpacksATarZstWithItsLinksAndRefusesEntriesOutsideTheFolder()
    {
        string root = NewRoot();
        string package = Path.Combine(root, "ollama.tar.zst");
        File.WriteAllBytes(package, TarZst(
            ("bin/", TarEntryType.Directory, null, null),
            ("bin/ollama", TarEntryType.RegularFile, Encoding.UTF8.GetBytes("program"), null),
            ("lib/ollama/libcudart.so.12.0", TarEntryType.RegularFile, Encoding.UTF8.GetBytes("library"), null),
            ("lib/ollama/libcudart.so.12", TarEntryType.SymbolicLink, null, "libcudart.so.12.0"),
            ("lib/ollama/escape", TarEntryType.SymbolicLink, null, "../../../outside"),
            ("../escaped.txt", TarEntryType.RegularFile, Encoding.UTF8.GetBytes("outside"), null)));

        string destination = Path.Combine(root, "out");
        OllamaRuntimeDependency.Extract(package, destination);

        Assert.AreEqual("program", File.ReadAllText(Path.Combine(destination, "bin", "ollama")));
        Assert.IsFalse(File.Exists(Path.Combine(root, "escaped.txt")));
        if (!OperatingSystem.IsWindows())
        {
            Assert.IsTrue(File.Exists(Path.Combine(destination, "bin", "ollama")));
            Assert.AreEqual("library", File.ReadAllText(Path.Combine(destination, "lib", "ollama", "libcudart.so.12")), "A link inside the folder is followed.");
            Assert.IsFalse(File.Exists(Path.Combine(destination, "lib", "ollama", "escape")), "A link that leaves the folder is not kept.");
        }
    }

    [TestMethod]
    public async Task Install_DownloadsVerifiesUnpacksAndPullsTheModel()
    {
        if (!(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64))
        {
            Assert.Inconclusive("The release asset of this test is the Linux x64 archive.");
        }

        string root = NewRoot();
        MakeInstalled(root, "0.40.3");
        byte[] archive = TarZst(("bin/ollama", TarEntryType.RegularFile, Encoding.UTF8.GetBytes("program"), null));
        string digest = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        string release = $$"""
            { "tag_name": "v0.40.4", "assets": [
              { "name": "ollama-linux-amd64.tar.zst", "browser_download_url": "{{Downloads}}ollama.tar.zst", "size": {{archive.Length}}, "digest": "sha256:{{digest}}" }
            ] }
            """;
        var pulls = new List<string>();
        var handler = new StubHandler(request =>
        {
            string url = request.RequestUri!.ToString();
            if (url == $"{Api}repos/ollama/ollama/releases/tags/v0.40.4")
            {
                return Json(release);
            }

            if (url == $"{Downloads}ollama.tar.zst")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
            }

            if (url == "http://localhost:11434/api/version")
            {
                return Json("{\"version\":\"0.40.4\"}");
            }

            if (url == "http://localhost:11434/api/pull")
            {
                pulls.Add(request.Content!.ReadAsStringAsync().Result);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"pulling manifest\"}\n{\"status\":\"success\"}\n", Encoding.UTF8, "application/x-ndjson"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var dependency = NewDependency(root, handler);
        var progress = new RecordingProgress();

        await dependency.InstallAsync("0.40.4", progress, CancellationToken.None);

        Assert.IsTrue(File.Exists(Path.Combine(root, "0.40.4", "bin", "ollama")));
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "0.40.3")), "The version it replaces is removed.");
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "downloads")), "The download is removed once it is unpacked.");
        Assert.AreEqual(Model, File.ReadAllText(Path.Combine(root, OllamaRuntimeDependency.ModelMarker)));
        Assert.AreEqual("0.40.4", dependency.InstalledVersion());
        Assert.AreEqual(1, pulls.Count);
        StringAssert.Contains(pulls[0], Model);
        Assert.IsTrue(progress.Lines.Any(line => line.Contains("ready")), "The install says it is ready.");
    }

    [TestMethod]
    public async Task Install_RefusesADownloadThatDoesNotMatchItsDigest()
    {
        if (!(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64))
        {
            Assert.Inconclusive("The release asset of this test is the Linux x64 archive.");
        }

        string root = NewRoot();
        byte[] archive = TarZst(("bin/ollama", TarEntryType.RegularFile, Encoding.UTF8.GetBytes("program"), null));
        string release = $$"""
            { "tag_name": "v0.40.4", "assets": [
              { "name": "ollama-linux-amd64.tar.zst", "browser_download_url": "{{Downloads}}ollama.tar.zst", "size": {{archive.Length}}, "digest": "sha256:{{new string('0', 64)}}" }
            ] }
            """;
        var handler = new StubHandler(request => request.RequestUri!.ToString() switch
        {
            var url when url == $"{Api}repos/ollama/ollama/releases/tags/v0.40.4" => Json(release),
            var url when url == $"{Downloads}ollama.tar.zst" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        await AssertAsync.ThrowsAsync<InvalidDataException>(() =>
            NewDependency(root, handler).InstallAsync("0.40.4", new Progress<string>(), CancellationToken.None));

        Assert.IsFalse(File.Exists(Path.Combine(root, "0.40.4", "bin", "ollama")), "Nothing that failed its checksum is unpacked.");
    }

    private static void WriteZipEntry(ZipArchive archive, string name, string content)
    {
        using Stream stream = archive.CreateEntry(name).Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    /// <summary>A release as the publisher packs it: a tar of the entries, compressed with zstd.</summary>
    private static byte[] TarZst(params (string Name, TarEntryType Type, byte[]? Data, string? LinkName)[] entries)
    {
        using var tar = new MemoryStream();
        using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach ((string name, TarEntryType type, byte[]? data, string? linkName) in entries)
            {
                var entry = new PaxTarEntry(type, name)
                {
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                };
                if (data is not null)
                {
                    entry.DataStream = new MemoryStream(data);
                }

                if (linkName is not null)
                {
                    entry.LinkName = linkName;
                }

                writer.WriteEntry(entry);
            }
        }

        tar.Position = 0;
        using var compressed = new MemoryStream();
        using (var zstd = new ZstdSharp.CompressionStream(compressed, leaveOpen: true))
        {
            tar.CopyTo(zstd);
        }

        return compressed.ToArray();
    }
}

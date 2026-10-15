using System.Net;
using System.Text;
using iTerminal.Core.AI;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Test.Services.RuntimeDependencies;

[TestClass]
public class OllamaExistingInstallTest
{
    private const string Model = "qwen3.5:0.8b";
    private static readonly List<string> Folders = [];

    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"iTerminal.OllamaExisting.{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        lock (Folders)
        {
            Folders.Add(folder);
        }

        return folder;
    }

    [ClassCleanup]
    public static void DeleteFolders()
    {
        lock (Folders)
        {
            foreach (string folder in Folders)
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly List<string> _requests = [];

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToList();
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_requests)
            {
                _requests.Add(request.RequestUri!.AbsolutePath);
            }

            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>A program called ollama in a folder of its own, standing for an install made outside iTerminal.</summary>
    private static string PutProgram()
    {
        string folder = NewFolder();
        File.WriteAllText(Path.Combine(folder, OperatingSystem.IsWindows() ? "ollama.exe" : "ollama"), "program");
        return folder;
    }

    private static OllamaRuntimeDependency NewDependency(string root, HttpMessageHandler handler, params string[] searchDirectories) =>
        new(root, Model, new OllamaServer(new Uri("http://localhost:11434/"), new HttpClient(handler)),
            new GitHubReleaseFeed(new HttpClient(handler), "https://api.github.com/"), new Version(0, 40, 4), searchDirectories);

    private static StubHandler Offline() => new(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

    [TestMethod]
    public void AnExistingProgramIsFoundButNotComplete_UntilTheModelIsThere()
    {
        string programs = PutProgram();
        var dependency = NewDependency(NewFolder(), Offline(), programs);

        Assert.IsNull(dependency.InstalledVersion(), "Without the model the AI bar still has nothing to talk to.");
        Assert.IsNotNull(dependency.BinaryPath(), "The program is the one the server job would start.");
        StringAssert.Contains(dependency.ExistingInstallNote!, "only the " + Model);
    }

    [TestMethod]
    public void AnExistingProgramWithTheModelMarkerIsInstalledAndNotUpdatedFromHere()
    {
        string programs = PutProgram();
        string root = NewFolder();
        File.WriteAllText(Path.Combine(root, OllamaRuntimeDependency.ModelMarker), Model);
        var dependency = NewDependency(root, Offline(), programs);

        Assert.IsNotNull(dependency.InstalledVersion());
        Assert.IsTrue(dependency.IsExistingInstall);
        StringAssert.Contains(dependency.ExistingInstallNote!, "already installed");
    }

    [TestMethod]
    public void NothingOnDiskAndNoServerIsMissing()
    {
        var dependency = NewDependency(NewFolder(), Offline(), NewFolder());

        Assert.IsNull(dependency.InstalledVersion());
        Assert.IsNull(dependency.BinaryPath());
        Assert.IsFalse(dependency.IsExistingInstall);
        Assert.IsNull(dependency.ExistingInstallNote);
    }

    [TestMethod]
    public void AProgramInsideTheDataFolderIsNotAnExistingInstall()
    {
        string root = NewFolder();
        File.WriteAllText(Path.Combine(root, OperatingSystem.IsWindows() ? "ollama.exe" : "ollama"), "program");

        Assert.IsNull(OllamaRuntimeDependency.FindExistingBinary([root], root));
    }

    [TestMethod]
    public async Task ARunningServerIsRecognisedWithItsVersionAndModels()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/version" => Json("{\"version\":\"0.9.1\"}"),
            "/api/tags" => Json("{\"models\":[{\"name\":\"qwen3.5:0.8b\"}]}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var dependency = NewDependency(NewFolder(), handler);

        // The check asks the server first; the release feed after it has nothing to say here.
        try
        {
            await dependency.ListVersionsAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
        }

        Assert.AreEqual("0.9.1", dependency.InstalledVersion());
        Assert.IsTrue(dependency.IsExistingInstall);
    }

    [TestMethod]
    public async Task Install_UsesTheExistingServerAndOnlyDownloadsTheModel()
    {
        string root = NewFolder();
        int pulls = 0;
        var handler = new StubHandler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/pull")
            {
                Interlocked.Increment(ref pulls);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"success\"}\n", Encoding.UTF8, "application/x-ndjson"),
                };
            }

            return path switch
            {
                "/api/version" => Json("{\"version\":\"0.9.1\"}"),
                "/api/tags" => Json("{\"models\":[]}"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        var dependency = NewDependency(root, handler);

        await dependency.InstallAsync("0.40.4", new Progress<string>(), CancellationToken.None);

        Assert.AreEqual(1, pulls);
        Assert.AreEqual(Model, File.ReadAllText(Path.Combine(root, OllamaRuntimeDependency.ModelMarker)));
        Assert.IsFalse(Directory.EnumerateDirectories(root).Any(), "No second copy of the program is downloaded.");
        Assert.AreEqual("0.9.1", dependency.InstalledVersion());
    }

    [TestMethod]
    public async Task Install_DoesNotPullAModelTheServerAlreadyHolds()
    {
        string root = NewFolder();
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/version" => Json("{\"version\":\"0.9.1\"}"),
            "/api/tags" => Json("{\"models\":[{\"name\":\"qwen3.5:0.8b\"}]}"),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        });
        var dependency = NewDependency(root, handler);

        await dependency.InstallAsync("0.40.4", new Progress<string>(), CancellationToken.None);

        Assert.IsFalse(handler.Requests.Contains("/api/pull"));
        Assert.IsTrue(File.Exists(Path.Combine(root, OllamaRuntimeDependency.ModelMarker)));
    }

    [TestMethod]
    public void ModelsDirectoryFor_IsOnlyGivenToTheProgramOfTheDataFolder()
    {
        string root = NewFolder();
        var dependency = NewDependency(root, Offline());

        Assert.AreEqual(Path.Combine(root, "models"), dependency.ModelsDirectoryFor(Path.Combine(root, "0.40.4", "bin", "ollama")));
        Assert.IsNull(dependency.ModelsDirectoryFor(Path.Combine(NewFolder(), "ollama")));
    }
}

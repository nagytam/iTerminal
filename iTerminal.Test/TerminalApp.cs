using iConsole.Interfaces;
using iConsole.Jobs;
using iConsole.Test.Core.Automation;
using iTerminal.Core.Application;
using iTerminal.Core.Data;
using iTerminal.Core.Jobs;
using iTerminal.Core.Screens;
using iTerminal.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace iTerminal.Test;

/// <summary>
/// The real iTerminal — <c>AddiTerminal</c>, <see cref="TerminalScreen"/>, every dialog and panel —
/// running on a <see cref="ConsoleAppDriver"/>, inside a sandbox of its own:
///
/// <list type="bullet">
/// <item>a fresh data folder (<see cref="iTerminalApplicationBuilderExtension.DataDirectoryKey"/>), so
/// the database, vault keys and playlists are new for every test and the user's own are never touched
/// — every test is also a first run;</item>
/// <item>two panel folders with known contents, the left panel focused on <see cref="LeftRoot"/>;</item>
/// <item>empty music and documents folders, so the indexers have nothing to walk.</item>
/// </list>
///
/// Three jobs are left out: the weather and quote pollers (they go to the internet) and the folder
/// indexer (it walks every drive on the machine). The network scan, the package manager and the quote
/// provider are <see cref="SandboxServices"/> fakes, so their dialogs can be driven without touching
/// the network or WinGet. Everything else runs as it does in production.
/// </summary>
public sealed class TerminalApp : IAsyncDisposable
{
    private static readonly Type[] OmittedJobs = [typeof(WeatherJob), typeof(OnlineQuoteJob), typeof(PortfolioQuoteJob), typeof(FolderIndexJob)];

    private readonly string _root;
    private bool _ownsSandbox = true;

    private TerminalApp(string root, ConsoleAppDriver driver)
    {
        _root = root;
        Driver = driver;
    }

    public ConsoleAppDriver Driver { get; }
    public string LeftRoot => Path.Combine(_root, "left");
    public string RightRoot => Path.Combine(_root, "right");
    public string DataDirectory => Path.Combine(_root, "data");

    /// <summary>The sandbox's own recycle bin (see <see cref="PutInRecycleBin"/>).</summary>
    public string RecycleBinFolder => Path.Combine(_root, "bin");

    /// <summary>
    /// Puts a file in the sandbox's recycle bin as if it had been deleted from
    /// <paramref name="originalPath"/> at <paramref name="deleted"/>: the content and the record
    /// beside it, as Windows writes them. <paramref name="content"/> null makes it a deleted folder.
    /// </summary>
    public static void PutInRecycleBin(string binFolder, string id, string originalPath, string? content, DateTime deleted)
    {
        Directory.CreateDirectory(binFolder);
        var extension = Path.GetExtension(originalPath);
        var contentPath = Path.Combine(binFolder, "$R" + id + extension);
        if (content is null)
        {
            Directory.CreateDirectory(contentPath);
            File.WriteAllText(Path.Combine(contentPath, "inside.txt"), "inside the deleted folder");
        }
        else
        {
            File.WriteAllText(contentPath, content);
        }

        File.WriteAllBytes(
            Path.Combine(binFolder, "$I" + id + extension),
            Core.Services.Recycle.WindowsRecycleBinStore.BuildRecord(originalPath, content?.Length ?? 0, deleted));
    }

    /// <summary>The screen as it is now.</summary>
    public ConsoleSnapshot Screen => Driver.Screen;

    /// <summary>
    /// Builds the sandbox, starts the app and waits until both panels list their folders and the
    /// splash screen is gone. <paramref name="seed"/> may add files to <see cref="LeftRoot"/> /
    /// <see cref="RightRoot"/> before the app reads them.
    /// </summary>
    public static async Task<TerminalApp> StartAsync(Action<string, string>? seed = null, int width = 140, int height = 40)
    {
        var root = Path.Combine(Path.GetTempPath(), "iTerminal.E2E." + Guid.NewGuid().ToString("N")[..8]);
        var left = Path.Combine(root, "left");
        var right = Path.Combine(root, "right");
        foreach (var folder in new[] { left, right, Path.Combine(root, "music"), Path.Combine(root, "documents") })
        {
            Directory.CreateDirectory(folder);
        }

        Directory.CreateDirectory(Path.Combine(left, "alpha"));
        File.WriteAllText(Path.Combine(left, "alpha", "inside_alpha.txt"), "inside alpha");
        Directory.CreateDirectory(Path.Combine(left, "beta"));
        File.WriteAllText(Path.Combine(left, "readme.txt"), "Hello from the left panel." + Environment.NewLine + "Second line.");
        File.WriteAllText(Path.Combine(left, "notes.md"), "# Notes");
        Directory.CreateDirectory(Path.Combine(right, "target"));
        File.WriteAllText(Path.Combine(right, "right_only.txt"), "right");
        seed?.Invoke(left, right);

        var app = await LaunchAsync(root, firstRun: true, width, height);
        try
        {
            await app.Driver.WaitUntilAsync(
                screen => screen.Contains("alpha") && screen.Contains("right_only.txt"),
                "both panels to list their folders");
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
        return app;
    }

    /// <summary>
    /// Shuts this instance down the way the host does at exit and starts a new one on the same
    /// sandbox — same database, same folders — which is what a user relaunching the app sees.
    /// This instance must not be used afterwards; dispose the returned one.
    /// </summary>
    public async Task<TerminalApp> RestartAsync()
    {
        _ownsSandbox = false;
        await Driver.DisposeAsync();
        SqliteConnection.ClearAllPools();
        var restarted = await LaunchAsync(_root, firstRun: false, Driver.Options.Width, Driver.Options.Height);
        try
        {
            await restarted.Driver.WaitUntilAsync(screen => screen.Contains(" entr"), "the panels to be drawn");
        }
        catch
        {
            await restarted.DisposeAsync();
            throw;
        }
        return restarted;
    }

    private static async Task<TerminalApp> LaunchAsync(string root, bool firstRun, int width, int height)
    {
        var options = new ConsoleAppDriverOptions
        {
            Width = width,
            Height = height,
            Timeout = TimeSpan.FromSeconds(20),
            Configuration = new Dictionary<string, string?>
            {
                [iTerminalApplicationBuilderExtension.DataDirectoryKey] = Path.Combine(root, "data"),
            },
        };

        ConsoleAppDriver driver;
        try
        {
            driver = await ConsoleAppDriver.StartAsync<TerminalScreen>((services, configuration) =>
            {
                services.AddiTerminal(configuration);

                foreach (var descriptor in services
                    .Where(d => d.ServiceType == typeof(IJob) && OmittedJobs.Contains(d.ImplementationType))
                    .ToList())
                {
                    services.Remove(descriptor);
                }

                // The production logger writes iTerminal.log into the working directory.
                services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);

                // Nothing reaches outside the machine: no network scan, no WinGet, no quote server.
                services.AddSingleton<INetworkDiscoveryService, SandboxServices.NetworkDiscovery>();
                services.AddSingleton<Core.Services.PackageManager.IPackageManagerService, SandboxServices.PackageManager>();
                services.AddSingleton<Core.Services.Portfolio.IQuoteProvider, SandboxServices.Quotes>();
                services.AddSingleton<SandboxServices.Shell>();
                services.AddSingleton<Core.Services.Shell.IInteractiveShellService>(provider => provider.GetRequiredService<SandboxServices.Shell>());
                services.AddSingleton<SandboxServices.Launcher>();
                services.AddSingleton<Core.Services.FileAssociations.IFileLauncher>(provider => provider.GetRequiredService<SandboxServices.Launcher>());
                services.AddSingleton<SandboxServices.DockerCli>();
                services.AddSingleton<Core.Services.Mail.IMailService, SandboxServices.Mail>();

                // A recycle bin of the sandbox's own, in the Windows format on every system: the
                // drive restores and deletes for good, and must never be let near the real one.
                services.AddSingleton<Core.Services.Recycle.IRecycleBinStore>(
                    new Core.Services.Recycle.WindowsRecycleBinStore(() => [Path.Combine(root, "bin")]));
                services.AddSingleton<Core.Services.Docker.IDockerDriveService>(provider =>
                    new Core.Services.Docker.DockerDriveService(provider.GetRequiredService<SandboxServices.DockerCli>()));

                // The sandbox has no person to type a master password, so it sets one up and unlocks the store itself,
                // on every launch — a restart unlocks again, as the console prompt would.
                services.AddSingleton<IHostedService>(provider => new UnlockPasswordManager(provider.GetRequiredService<IPasswordManager>()));

                if (firstRun)
                {
                    // After the database is initialised and before the desktop reads anything: this
                    // is where a real install's settings would already be.
                    services.AddSingleton<IHostedService>(provider => new SeedSettings(
                        provider.GetRequiredService<ISettingsService>(),
                        new Dictionary<string, string>
                        {
                            [Core.Services.ContentModelService.Left] = "file://" + Path.Combine(root, "left"),
                            [Core.Services.ContentModelService.Right] = "file://" + Path.Combine(root, "right"),
                            [Settings.MusicFolder] = Path.Combine(root, "music"),
                            [Settings.DocumentsFolder] = Path.Combine(root, "documents"),
                        }));
                }
            }, options);
        }
        catch
        {
            if (firstRun)
            {
                DeleteQuietly(root);
            }
            throw;
        }

        var app = new TerminalApp(root, driver);
        try
        {
            await app.DismissSplashAsync();
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
        return app;
    }

    #region Driving
    public Task<bool> PressAsync(string chord) => Driver.PressAsync(chord);
    public Task PressSequenceAsync(params string[] chords) => Driver.PressSequenceAsync(chords);
    public Task TypeAsync(string text) => Driver.TypeAsync(text);
    public Task<ConsoleSnapshot> WaitForTextAsync(string text, TimeSpan? timeout = null) => Driver.WaitForTextAsync(text, timeout);
    public Task<ConsoleSnapshot> WaitForTextGoneAsync(string text, TimeSpan? timeout = null) => Driver.WaitForTextGoneAsync(text, timeout);
    public Task<ConsoleSnapshot> WaitUntilAsync(Func<ConsoleSnapshot, bool> condition, string because, TimeSpan? timeout = null) =>
        Driver.WaitUntilAsync(condition, because, timeout);
    public Task WaitUntilAsync(Func<bool> condition, string because, TimeSpan? timeout = null) =>
        Driver.WaitUntilAsync(condition, because, timeout);

    public IContentModelService ContentModels => Driver.GetRequiredService<IContentModelService>();

    /// <summary>The model of the panel that has the keyboard.</summary>
    public IContentModel? FocusedModel => ContentModels.GetFocusedModel();

    /// <summary>The name of the entry under the focused panel's cursor.</summary>
    public string? FocusedEntryName => FocusedModel?.GetFocusedEntry()?.Name;

    /// <summary>
    /// Moves the focused panel's cursor onto <paramref name="name"/> with Home and Down, the way a
    /// user walks to a file, and fails if the listing never offers it.
    /// </summary>
    public async Task SelectEntryAsync(string name)
    {
        await WaitUntilAsync(
            () => FocusedModel?.FilteredEntries.Any(e => e.Name == name) == true,
            $"'{name}' to be listed in the focused panel");
        await PressAsync("Home");
        for (var step = 0; step < 100 && FocusedEntryName != name; step++)
        {
            await PressAsync("Down");
        }

        if (FocusedEntryName != name)
        {
            throw new AssertFailedException($"Could not move the cursor onto '{name}'.{Environment.NewLine}{Screen.Dump()}");
        }
    }

    /// <summary>Waits until the focused panel lists the folder at <paramref name="path"/>.</summary>
    public Task WaitForFocusedFolderAsync(string path) =>
        WaitUntilAsync(
            () => string.Equals(
                Path.TrimEndingDirectorySeparator(FocusedModel?.Url.Path ?? string.Empty),
                Path.TrimEndingDirectorySeparator(path),
                StringComparison.OrdinalIgnoreCase),
            $"the focused panel to show {path}");

    #region Reading the screen
    /// <summary>Width of one panel: the screen is split in two.</summary>
    public int PanelWidth => Screen.Width / 2;

    /// <summary>The left panel's rows, frame included.</summary>
    public IReadOnlyList<string> LeftPanel(ConsoleSnapshot? screen = null) =>
        (screen ?? Screen).Region(0, 1, PanelWidth, PanelHeight(screen ?? Screen));

    /// <summary>The right panel's rows, frame included.</summary>
    public IReadOnlyList<string> RightPanel(ConsoleSnapshot? screen = null) =>
        (screen ?? Screen).Region(PanelWidth, 1, PanelWidth, PanelHeight(screen ?? Screen));

    /// <summary>Rows from the panels' top border to their bottom border.</summary>
    private static int PanelHeight(ConsoleSnapshot screen)
    {
        for (var y = screen.Height - 1; y > 1; y--)
        {
            if (screen.CharAt(0, y) == '╚')
            {
                return y;
            }
        }
        return screen.Height - 2;
    }

    /// <summary>Whether <paramref name="name"/> is listed in the left panel.</summary>
    public bool LeftPanelLists(string name, ConsoleSnapshot? screen = null) =>
        LeftPanel(screen).Any(row => row.Length > 1 && row[1..].StartsWith(name + " ", StringComparison.Ordinal));

    /// <summary>Whether <paramref name="name"/> is listed in the right panel.</summary>
    public bool RightPanelLists(string name, ConsoleSnapshot? screen = null) =>
        RightPanel(screen).Any(row => row.Length > 1 && row[1..].StartsWith(name + " ", StringComparison.Ordinal));

    /// <summary>
    /// Asserts that no window frame is drawn anywhere but the two panels' own borders — what is left
    /// on screen by a dialog that was closed but not cleaned up.
    /// </summary>
    public void AssertNoStrayFrames(ConsoleSnapshot? screen = null)
    {
        screen ??= Screen;
        var stray = FindStrayFrame(screen);
        if (stray is not null)
        {
            throw new AssertFailedException(
                $"A frame is drawn at ({stray.X},{stray.Y}), outside the panels' borders — the remains of a closed window?{Environment.NewLine}{screen.Dump()}");
        }
    }

    /// <summary>
    /// Waits for a frame with no window drawn outside the panels. Use this rather than
    /// <see cref="AssertNoStrayFrames"/> straight after closing something: the renderer writes a
    /// frame as several runs of cells, so one snapshot can catch it half old and half new. A ghost
    /// that is really there never goes away, and fails the wait.
    /// </summary>
    public async Task WaitForNoStrayFramesAsync()
    {
        try
        {
            await WaitUntilAsync(screen => FindStrayFrame(screen) is null, "no window frames outside the panels");
        }
        catch (AssertFailedException)
        {
            AssertNoStrayFrames();
            throw;
        }
    }

    private iConsole.Data.Position? FindStrayFrame(ConsoleSnapshot screen)
    {
        var half = screen.Width / 2;
        var panelEdges = new HashSet<int> { 0, half - 1, half, screen.Width - 1 };
        for (var y = 1; y < screen.Height; y++)
        {
            for (var x = 0; x < screen.Width; x++)
            {
                if ("╔╗╚╝║".Contains(screen.CharAt(x, y)) && !panelEdges.Contains(x))
                {
                    return new iConsole.Data.Position(x, y);
                }
            }
        }
        return null;
    }

    /// <summary>Waits until no dialog is open: the keys go to the screen again.</summary>
    public Task WaitForNoDialogAsync() =>
        WaitUntilAsync(() => Driver.FocusedModal is TerminalScreen, "every dialog to be closed");
    #endregion

    private async Task DismissSplashAsync()
    {
        // The splash closes itself after three seconds; Esc closes it at once. Either way the
        // panels are what the first real frame has to show.
        const string Splash = "Welcome to iTerminal";
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (Screen.Contains(Splash) && Driver.FocusedModal is iConsole.Dialogs.SplashDialog)
            {
                await PressAsync("Esc");
                break;
            }
            if (!Screen.Contains(Splash) && Driver.FocusedModal is not iConsole.Dialogs.SplashDialog)
            {
                break;
            }
            await Task.Delay(20);
        }
        await Driver.WaitForTextGoneAsync(Splash);
    }
    #endregion

    public async ValueTask DisposeAsync()
    {
        await Driver.DisposeAsync();
        if (_ownsSandbox)
        {
            SqliteConnection.ClearAllPools();
            DeleteQuietly(_root);
        }
    }

    private static void DeleteQuietly(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>Writes settings after the database exists and before the desktop starts.</summary>
    private sealed class UnlockPasswordManager(IPasswordManager passwordManager) : IHostedService
    {
        private const string SandboxMasterPassword = "sandbox master password";

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!passwordManager.IsSetUp)
            {
                passwordManager.Setup(SandboxMasterPassword);
            }

            passwordManager.Unlock(SandboxMasterPassword);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SeedSettings(ISettingsService settingsService, IReadOnlyDictionary<string, string> values) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var (key, value) in values)
            {
                settingsService.Set(key, value);
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

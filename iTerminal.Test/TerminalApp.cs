using iConsole.Interfaces;
using iConsole.Jobs;
using iTerminal.Widgets.Application;
using iTerminal.DevelopmentStudio.Application;
using iTerminal.Widgets.Jobs;
using iConsole.Test.Core.Automation;
using iTerminal.Core.Application;
using iTerminal.Core.Commands;
using iTerminal.Core.Data;
using iTerminal.Core.Jobs;
using iTerminal.Core.Screens;
using iTerminal.Core.Services;
using iTerminal.Core.Services.KeyBindings;
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
/// Four jobs are left out: the weather and quote pollers and the connectivity check (they go to the internet) and the folder
/// indexer (it walks every drive on the machine). The network scan, the package manager and the quote
/// provider are <see cref="SandboxServices"/> fakes, so their dialogs can be driven without touching
/// the network or WinGet. Everything else runs as it does in production.
/// </summary>
public sealed class TerminalApp : IAsyncDisposable
{
    private static readonly Type[] OmittedJobs = [typeof(WeatherJob), typeof(OnlineQuoteJob), typeof(InternetConnectivityJob), typeof(FolderIndexJob), typeof(RuntimeDependencyCheckJob), typeof(UpdateCheckJob), typeof(OllamaServerJob)];

    /// <summary>How long a test may run (<c>[Timeout]</c> is 120 s): an app older than this belongs to a test that was abandoned.</summary>
    private static readonly TimeSpan LeakAge = TimeSpan.FromSeconds(150);

    // MSTest abandons a test that times out without running its <c>await using</c>, so its app, with every job, would keep
    // polling for the rest of the run and slow each test after it. Every live app is listed here and the abandoned ones are swept.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<TerminalApp, DateTime> Live = new();

    private readonly string _root;
    private bool _ownsSandbox = true;

    private TerminalApp(string root, ConsoleAppDriver driver)
    {
        _root = root;
        Driver = driver;
        Live[this] = DateTime.UtcNow;
    }

    /// <summary>Disposes the apps of tests that timed out (or, with <paramref name="all"/>, every app that is still up).</summary>
    public static async Task SweepLeakedAsync(bool all = false)
    {
        DateTime cutoff = DateTime.UtcNow - LeakAge;
        foreach (KeyValuePair<TerminalApp, DateTime> entry in Live.ToArray())
        {
            if (all || entry.Value < cutoff)
            {
                await entry.Key.DisposeAsync();
            }
        }
    }

    public ConsoleAppDriver Driver { get; }
    public string LeftRoot => Path.Combine(_root, "left");
    public string RightRoot => Path.Combine(_root, "right");
    public string DataDirectory => Path.Combine(_root, "data");

    /// <summary>The sandbox's own recycle bin (see <see cref="PutInRecycleBin"/>).</summary>
    public string RecycleBinFolder => Path.Combine(_root, "bin");

    /// <summary>The sandbox's Trash on Linux, where a delete with the bin ticked goes (its <c>files</c> and <c>info</c> folders).</summary>
    public string TrashFolder => Path.Combine(_root, "trash");

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
    /// <see cref="RightRoot"/> before the app reads them. <paramref name="keyMapJson"/>, if given, is written to
    /// <c>keymap.json</c> in the data folder before the first start.
    /// </summary>
    public static async Task<TerminalApp> StartAsync(Action<string, string>? seed = null, int width = 140, int height = 40, string? keyMapJson = null)
    {
        await SweepLeakedAsync();
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
        if (keyMapJson is not null)
        {
            Directory.CreateDirectory(Path.Combine(root, "data"));
            File.WriteAllText(Path.Combine(root, "data", KeyMapFile.FileName), keyMapJson);
        }

        var app = await LaunchAsync(root, firstRun: true, width, height);
        try
        {
            await app.Driver.WaitUntilAsync(
                // "right_only" and not the whole name: on a very narrow terminal the right panel cuts the name.
                screen => screen.Contains("alpha") && screen.Contains("right_only"),
                "both panels to list their folders");

            // The listings are on screen before the left panel has its cursor and the keyboard; a key sent in that gap is lost,
            // which is how "Down and Up move the cursor" and "Ctrl+PgUp goes to the parent folder" failed now and then on a busy CI machine.
            await app.WaitUntilAsync(() => app.FocusedEntryName is not null, "the left panel to have a cursor");
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
        Live.TryRemove(this, out _);
        await Driver.DisposeAsync();
        SqliteConnection.ClearAllPools();
        var restarted = await LaunchAsync(_root, firstRun: false, Driver.Options.Width, Driver.Options.Height);
        try
        {
            // " entr" is "entries" in English, " Eintr" the start of "Einträge" in German: the relaunch may be in either language.
            await restarted.Driver.WaitUntilAsync(screen => screen.Contains(" entr") || screen.Contains(" Eintr"), "the panels to be drawn");
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
            Timeout = TimeSpan.FromSeconds(30),
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
                services.AddWidgets(configuration);
                services.AddDevelopmentStudio();

                foreach (var descriptor in services
                    .Where(d => d.ServiceType == typeof(IJob) && OmittedJobs.Contains(d.ImplementationType))
                    .ToList())
                {
                    services.Remove(descriptor);
                }

                // The production logger writes iTerminal.log into the working directory.
                services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
                // The terminal's progress sequence never reaches the developer's own terminal during a run.
                services.AddSingleton<iConsole.Services.ITerminalProgressService, SandboxServices.TerminalProgress>();

                // Path completion lists only the sandbox's own folders, never the machine's.
                services.AddSingleton<IDirectoryLister>(new SandboxServices.DirectoryLister(root));

                // Nothing reaches outside the machine: no network scan, no WinGet, no quote server.
                services.AddSingleton<INetworkDiscoveryService, SandboxServices.NetworkDiscovery>();
                services.AddSingleton<Core.Services.PackageManager.IPackageManagerService, SandboxServices.PackageManager>();
                services.AddSingleton<Core.Services.RuntimeDependencies.IRuntimeDependencyService, SandboxServices.Dependencies>();
                services.AddSingleton<Widgets.Services.Stocks.IStockQuoteProvider, SandboxServices.StockQuotes>();
                // Evidence entries are time-stamped by no authority: no request leaves the sandbox.
                services.AddSingleton<Core.Services.Timestamps.ITimestampAuthority, SandboxServices.TimestampAuthority>();
                // Voice input records from no microphone and recognises with no model: the real service runs on stand-ins.
                services.AddSingleton<iConsole.Media.Speech.ISpeechRecognizer, SandboxServices.SpeechRecognizer>();
                services.AddSingleton<iConsole.Media.Speech.IMicrophoneCaptureFactory, SandboxServices.Microphone>();
                // Neither the translation API nor the local model is asked: both providers give way to one that answers at once.
                Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
                    .RemoveAll<Widgets.Services.Translation.ITranslationProvider>(services);
                services.AddSingleton<Widgets.Services.Translation.ITranslationProvider, SandboxServices.Translation>();
                // AI queries in the file viewers: the model is not asked, a fixed draft answers at once, so no run reaches an AI provider.
                Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
                    .RemoveAll<iConsole.Tabular.IGridQueryAssistant>(services);
                services.AddSingleton<iConsole.Tabular.IGridQueryAssistant, SandboxServices.GridQueryAssistant>();
                services.AddSingleton<Widgets.Services.Currency.ICurrencyRateProvider, SandboxServices.CurrencyRates>();
                // Tools > Weather: the forecast and the place search are answered from a fixed table, so no run reaches Open-Meteo.
                Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
                    .RemoveAll<Widgets.Services.Weather.IForecastProvider>(services);
                services.AddSingleton<Widgets.Services.Weather.IForecastProvider, SandboxServices.WeatherForecasts>();
                services.AddSingleton<Widgets.Services.Encyclopedia.IEncyclopediaProvider, SandboxServices.Encyclopedia>();
                // The network tools would ping, resolve and query whois for the typed target: a fixed answer stands in, so no run reaches the network.
                services.AddSingleton<Core.Services.NetworkTools.INetworkToolService, SandboxServices.NetworkToolRunner>();
                // The file transfer would bind a port on the LAN and serve the selection: a fixed share stands in, so no run listens on a socket.
                Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
                    .RemoveAll<Core.Services.Sharing.IFileShareService>(services);
                services.AddSingleton<Core.Services.Sharing.IFileShareService, SandboxServices.FileShareStub>();
                services.AddSingleton<Widgets.Services.YouTube.IYouTubeService, SandboxServices.YouTube>();
                // The benchmark's probes read the disk and the network: each one gives a fixed figure, so no run measures this machine.
                Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
                    .RemoveAll<Core.Services.Benchmark.IBenchmarkProbe>(services);
                foreach (Core.Services.Benchmark.IBenchmarkProbe probe in SandboxServices.BenchmarkProbes.All)
                {
                    services.AddSingleton(probe);
                }
                services.AddSingleton<SandboxServices.Shell>();
                // The command log's shell session is a fake: no test starts a real sh or bash.
                services.AddSingleton<Core.Services.Shell.ILocalShellSessionService, SandboxServices.LocalShellSessions>();
                services.AddSingleton<Core.Services.Shell.IInteractiveShellService>(provider => provider.GetRequiredService<SandboxServices.Shell>());
                services.AddSingleton<SandboxServices.Launcher>();
                services.AddSingleton<Core.Services.FileAssociations.IFileLauncher>(provider => provider.GetRequiredService<SandboxServices.Launcher>());
                services.AddSingleton<SandboxServices.DockerCli>();
                services.AddSingleton<SandboxServices.VirtCli>();
                services.AddSingleton<Core.Services.VirtualMachines.IVirtualMachineService>(provider =>
                {
                    // Every backend runs on the sandboxed CLI, whichever operating system the suite is on, so no test
                    // reaches a real hypervisor. Only the system libvirt answers; the other two are left out of the list.
                    var cli = provider.GetRequiredService<SandboxServices.VirtCli>();
                    return new Core.Services.VirtualMachines.CombinedVirtualMachineService(
                    [
                        new Core.Services.VirtualMachines.LibvirtVirtualMachineService(cli, cli.LaunchViewer),
                        new Core.Services.VirtualMachines.LibvirtVirtualMachineService(cli, cli.LaunchViewer,
                            Core.Services.VirtualMachines.LibvirtVirtualMachineService.SessionConnection,
                            Core.Services.VirtualMachines.LibvirtVirtualMachineService.BoxesLabel),
                        new Core.Services.VirtualMachines.VirtualBoxVirtualMachineService(cli, cli.LaunchViewer),
                    ]);
                });

                // No test asks the machine it runs on for its phones: mtp:// is there, and empty-handed.
                services.AddSingleton<Core.Services.Mtp.IMtpBackend>(
                    new Core.Services.Mtp.UnavailableMtpBackend("No MTP devices in the test sandbox."));

                // No test browses a phone or a headset over Bluetooth: obex:// is there, and empty-handed.
                services.AddSingleton<Core.Services.Obex.IObexBackend>(
                    new Core.Services.Obex.UnavailableObexBackend("No Bluetooth file transfer in the test sandbox."));

                // No test reaches Dropbox or Azure: their clients are there, and every request they make is refused.
                services.AddSingleton<Core.Services.Cloud.IDropboxFileSystemService>(
                    new Core.Services.Cloud.DropboxFileSystemService(
                        new Core.Services.Cloud.DropboxTokenProvider(new Core.Services.Cloud.OAuthClientOptions()),
                        new SandboxServices.RefusedNetwork(),
                        "https://api.dropboxapi.com",
                        "https://content.dropboxapi.com"));
                services.AddSingleton<Core.Services.Cloud.IAzureBlobFileSystemService>(
                    new Core.Services.Cloud.AzureBlobFileSystemService(
                        account => new Core.Services.Cloud.AzureBlobCredentials(account, null, null),
                        new SandboxServices.RefusedNetwork()));

                // No test reaches an NFS server: nfs:// is there, and every mount is refused.
                services.AddSingleton<Core.Services.Nfs.INfsMountFactory>(
                    new Core.Services.Nfs.UnavailableNfsMountFactory("No NFS servers in the test sandbox."));

                // The clipboard stays in the process — a run must neither overwrite what the person
                // running it had copied nor paste what they had — and no test ejects, formats or
                // mounts anything of the machine it runs on.
                services.AddSingleton<Core.Services.Clipboard.IFileClipboardService, Core.Services.Clipboard.InProcessFileClipboardService>();
                services.AddSingleton<iConsole.Services.ITextClipboardService, iConsole.Services.InProcessTextClipboardService>();
                services.AddSingleton<SandboxServices.DriveActions>();
                services.AddSingleton<Core.Services.Drives.IDriveActionsService>(provider => provider.GetRequiredService<SandboxServices.DriveActions>());
                services.AddSingleton<Core.Services.AccessControl.IAccessControlService, SandboxServices.AccessControl>();
                // No test reads or removes the download mark of a file of the machine it runs on.
                services.AddSingleton<SandboxServices.FileOrigins>();
                services.AddSingleton<Core.Services.Origin.IFileOriginService>(provider => provider.GetRequiredService<SandboxServices.FileOrigins>());
                // No test scans for, pairs with or forgets a device of the machine it runs on.
                services.AddSingleton<Core.Services.Bluetooth.IBluetoothService, SandboxServices.Bluetooth>();
                // No test prints on, or lists the queue of, a printer of the machine it runs on.
                services.AddSingleton<SandboxServices.Printing>();
                services.AddSingleton<Core.Services.Printing.IPrintService>(provider => provider.GetRequiredService<SandboxServices.Printing>());
                // No test lists or scans on a scanner of the machine it runs on.
                services.AddSingleton<SandboxServices.Scanning>();
                services.AddSingleton<Core.Services.Scanning.IScanService>(provider => provider.GetRequiredService<SandboxServices.Scanning>());
                // No test lists, reads or deletes the alternate data streams or attributes of a real file.
                services.AddSingleton<SandboxServices.StreamAttributes>();
                services.AddSingleton<Core.Services.Streams.IStreamAttributeService>(provider => provider.GetRequiredService<SandboxServices.StreamAttributes>());
                services.AddSingleton<Core.Services.Mail.IMailService, SandboxServices.Mail>();
                services.AddSingleton<Core.Services.Web.IWebFetcher, SandboxServices.Web>();
                // No test asks VirusTotal about anything.
                services.AddSingleton<SandboxServices.HashReputation>();
                services.AddSingleton<Core.Services.Reputation.IHashReputationService>(provider => provider.GetRequiredService<SandboxServices.HashReputation>());
                services.AddSingleton<SandboxServices.SystemSettings>();
                services.AddSingleton<Core.Services.SystemSettings.ISessionEnvironment, SandboxServices.SessionEnvironment>();
                services.AddSingleton<Core.Services.SystemSettings.ISystemSettingsService>(provider => provider.GetRequiredService<SandboxServices.SystemSettings>());
                services.AddSingleton<SandboxServices.SystemSettings.UsersAndGroups>();
                services.AddSingleton<Core.Services.UsersAndGroups.IUserGroupService>(provider => provider.GetRequiredService<SandboxServices.SystemSettings.UsersAndGroups>());
                services.AddSingleton<SandboxServices.MalwareScanner>();
                services.AddSingleton<Core.Services.Defender.IMalwareScanService>(provider => provider.GetRequiredService<SandboxServices.MalwareScanner>());

                // A recycle bin of the sandbox's own, in the Windows format on every system: the
                // drive restores and deletes for good, and must never be let near the real one.
                services.AddSingleton<Core.Services.Recycle.IRecycleBinStore>(
                    new Core.Services.Recycle.WindowsRecycleBinStore(() => [Path.Combine(root, "bin")]));

                // Where a local delete with the bin ticked goes on Linux: a Trash of the sandbox's own,
                // and no per-volume ones, so a run never puts anything in the user's real one.
                services.AddSingleton<Core.Services.Recycle.ITrashService>(
                    new Core.Services.Recycle.FreedesktopTrash(Path.Combine(root, "trash"), () => [], 0));
                services.AddSingleton<Core.Services.Docker.IDockerDriveService>(provider =>
                    new Core.Services.Docker.DockerDriveService(provider.GetRequiredService<SandboxServices.DockerCli>()));

                // Program.cs unlocks the store before the screen opens; the sandbox builds its own host, so it does the same,
                // on every launch.
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
                            // The memory drive mirrors itself to a folder under the system temp; left at its
                            // default, files copied onto it by one run were loaded by the next.
                            [nameof(Core.Jobs.MemoryDriveSynchronizationJob) + "SyncPath"] = Path.Combine(root, "memory"),
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
    /// <summary>
    /// Opens the language selector, picks the second language (Deutsch) and waits until the screen is German. Fixed pauses
    /// here let the next key arrive while the selector was still closing, and the key was lost.
    /// </summary>
    public async Task SwitchToGermanAsync()
    {
        Assert.IsTrue(await PressAsync("Ctrl+Shift+F3"));
        await WaitUntilAsync(() => Driver.FocusedModal is Core.Dialogs.LanguageDialog, "the language dialog to open");
        await PressAsync("Down");
        await PressAsync("Enter");
        await WaitForNoDialogAsync();
        await WaitUntilAsync(() => Driver.GetRequiredService<iConsole.Localization.ILocalizer>().LanguageCode == "de", "the language to change to German");
        await WaitForNoStrayFramesAsync();
    }

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

    /// <summary>True when the cursor is on the <c>..</c> row, whose <see cref="IEntry.Name"/> is the parent's name, not <c>..</c>.</summary>
    public bool ParentRowFocused => FocusedModel?.GetFocusedEntry() is { } entry && FocusedEntry.IsParentEntry(entry);

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

    /// <summary>
    /// Moves the open Tools drop-down onto the submenu titled <paramref name="title"/> and opens it with Right. A submenu's
    /// row carries a ▶ and is drawn as "&lt;Title ▶&gt;" while selected; its entries are drawn only once it is open, so a test
    /// that looks for one of them calls this first.
    /// </summary>
    public async Task OpenSubmenuAsync(string title)
    {
        for (int attempt = 0; attempt < 100 && !Screen.Lines.Any(line => line.Contains("<" + title, StringComparison.Ordinal) && line.Contains("▶>", StringComparison.Ordinal)); attempt++)
        {
            ConsoleSnapshot previous = Screen;
            await PressAsync("Down");
            await Driver.WaitUntilAsync(screen => !screen.HasSameContent(previous), "the Tools menu to redraw after moving selection", TimeSpan.FromSeconds(2));
        }

        await PressAsync("Right");
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
    public async Task WaitForNoDialogAsync()
    {
        try
        {
            await WaitUntilAsync(() => Driver.FocusedModal is TerminalScreen, "every dialog to be closed");
        }
        catch (AssertFailedException exception)
        {
            // A progress window that stays up is usually a task that failed: its message alone says too little to find the cause.
            var failures = Driver.GetRequiredService<iConsole.Tasks.TaskManager>().Tasks
                .OfType<iConsole.Tasks.BackgroundTask>()
                .Where(task => task.Failure is not null)
                .Select(task => task.Name + ": " + task.Failure);
            string detail = string.Join(Environment.NewLine, failures);
            throw detail.Length == 0 ? exception : new AssertFailedException(exception.Message + Environment.NewLine + detail, exception);
        }
    }
    #endregion

    private async Task DismissSplashAsync()
    {
        // The splash closes itself after 4.5 seconds; Esc closes it at once. Either way the
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
        Live.TryRemove(this, out _);
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
        public Task StartAsync(CancellationToken cancellationToken)
        {
            passwordManager.Unlock();
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

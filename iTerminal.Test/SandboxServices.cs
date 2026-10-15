using System.Net;
using System.Runtime.CompilerServices;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Model.PackageManager;
using iTerminal.Core.Services;
using iTerminal.Core.Services.PackageManager;
using iTerminal.Core.Services.Portfolio;

namespace iTerminal.Test;

/// <summary>
/// Stand-ins for the services that reach outside the machine's own folders: the network scan, the
/// package manager, the stock quotes and the Docker daemon. The sandbox registers them for every
/// test, so no test ever probes the local network, runs WinGet, calls a quote server or lists the
/// machine's containers — and the dialogs built on them
/// can be driven like any other.
/// </summary>
public static class SandboxServices
{
    /// <summary>The host the fake network scan finds.</summary>
    public static readonly IPAddress HostAddress = IPAddress.Parse("192.0.2.17");

    /// <summary>A network with one host offering SSH and an SMB share.</summary>
    public sealed class NetworkDiscovery : INetworkDiscoveryService
    {
        public async IAsyncEnumerable<DiscoveredHost> ScanAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new DiscoveredHost(HostAddress, SshAvailable: true, SmbAvailable: true, Shares: ["photos"]);
        }
    }

    /// <summary>
    /// A mail service with one inbox message and an address book of one person, and nothing reaching a server.
    /// The sandbox has no mail account, so the dialog opens on its setup form; a test that configures one gets
    /// these canned answers rather than a POP3, SMTP or LDAP server.
    /// </summary>
    public sealed class Mail : Core.Services.Mail.IMailService
    {
        private static readonly Core.Model.Mail.MailListing Welcome =
            new(1, 420, "Ada <ada@example.test>", "Welcome", "Tue, 14 Oct 2026 09:00:00 +0200");

        public Task<IReadOnlyList<Core.Model.Mail.MailListing>> ListAsync(Core.Model.Mail.MailAccount account, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Core.Model.Mail.MailListing>>([Welcome]);

        public Task<Core.Model.Mail.MailItem> ReadAsync(Core.Model.Mail.MailAccount account, string password, int number, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Core.Model.Mail.MailItem(number, Welcome.From, "you@example.test", Welcome.Subject, Welcome.Date, "Hello from the sandbox."));

        public Task DeleteAsync(Core.Model.Mail.MailAccount account, string password, int number, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendAsync(Core.Model.Mail.MailAccount account, string password, string to, string subject, string body, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Core.Model.Mail.DirectoryPerson>> SearchDirectoryAsync(Core.Model.Mail.MailAccount account, string password, string term, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Core.Model.Mail.DirectoryPerson>>([new("Ada Lovelace", "ada@example.test", string.Empty)]);
    }

    /// <summary>Two packages installed, two to be found, and an install that reports and succeeds.</summary>
    public sealed class PackageManager : IPackageManagerService
    {
        private readonly List<PackageInfo> _installed =
        [
            new("Git", "Git.Git", "2.45.0", "2.46.0", "winget"),
            new("Node.js", "OpenJS.NodeJS", "20.11.0", null, "winget"),
        ];

        private readonly PackageInfo[] _available =
        [
            new("Python 3.12", "Python.Python.3.12", "3.12.4", null, "winget"),
            new("Python Launcher", "Python.Launcher", "3.12.4", null, "winget"),
        ];

        public string DisplayName => "FakePM";

        public Task<PackageQueryResult> GetInstalledPackagesAsync(CancellationToken cancellationToken = default)
        {
            lock (_installed)
            {
                return Task.FromResult(PackageQueryResult.Ok([.. _installed]));
            }
        }

        public Task<PackageQueryResult> SearchPackagesAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult(PackageQueryResult.Ok(
                _available.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList()));

        public Task<int> InstallAsync(string packageId, Action<string> onOutputLine, CancellationToken cancellationToken = default)
        {
            onOutputLine($"Installing {packageId}…");
            onOutputLine("Successfully installed");
            lock (_installed)
            {
                _installed.AddRange(_available.Where(p => p.Id == packageId));
            }
            return Task.FromResult(0);
        }

        public Task<int> UninstallAsync(string packageId, Action<string> onOutputLine, CancellationToken cancellationToken = default)
        {
            onOutputLine($"Removing {packageId}…");
            lock (_installed)
            {
                _installed.RemoveAll(p => p.Id == packageId);
            }
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// The docker CLI, answering from a script: one running container, <c>web</c>, holding
    /// <c>/etc/motd</c>. The real <see cref="Core.Services.Docker.DockerDriveService"/> runs on top of it,
    /// so what the panels show is the drive's own parsing of real-shaped output — only the daemon is
    /// missing, which is also what keeps a test run from touching the machine's containers.
    /// </summary>
    public sealed class DockerCli : Core.Services.Processes.IExternalProcessRunner
    {
        public const string Motd = "Welcome to the web container.";
        public const string LastLogLine = "GET /index.html 200";

        private volatile bool _running = true;

        /// <summary>Every start, stop and restart, in order — what the keys actually asked docker for.</summary>
        public System.Collections.Concurrent.ConcurrentQueue<string> Actions { get; } = new();

        public Task<Core.Services.Processes.ProcessOutput> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory = null, CancellationToken cancellationToken = default)
        {
            var line = string.Join(' ', arguments);
            if (line is "start web" or "stop web" or "restart web")
            {
                Actions.Enqueue(line);
                _running = !line.StartsWith("stop", StringComparison.Ordinal);
            }

            var stdout = line switch
            {
                _ when line.StartsWith("ps ", StringComparison.Ordinal) => _running
                    ? "web\trunning\tUp 2 hours\tnginx:latest\t2026-10-04 08:00:00 +0200 CEST\n"
                    : "web\texited\tExited (0) 1 second ago\tnginx:latest\t2026-10-04 08:00:00 +0200 CEST\n",
                "start web" or "stop web" or "restart web" => "web\n",
                "logs --tail 200 --timestamps web" =>
                    "2026-10-05T08:00:00.000000000Z nginx started\n" +
                    $"2026-10-05T08:00:01.000000000Z {LastLogLine}\n",
                "exec web ls -lAnL -- /" =>
                    "drwxr-xr-x 2 0 0 4096 Oct  4 08:00 etc\n",
                "exec web ls -lAnL -- /etc" =>
                    $"-rw-r--r-- 1 0 0 {Motd.Length} Oct  4 08:00 motd\n",
                _ => null,
            };
            return Task.FromResult(stdout is null
                ? new Core.Services.Processes.ProcessOutput(1, string.Empty, $"unexpected: docker {line}")
                : new Core.Services.Processes.ProcessOutput(0, stdout, string.Empty));
        }

        public async Task<Core.Services.Processes.ProcessOutput> RunToStreamAsync(string fileName, IReadOnlyList<string> arguments, Stream destination, string? workingDirectory = null, Func<Stream, Task>? writeStandardInput = null, CancellationToken cancellationToken = default)
        {
            var line = string.Join(' ', arguments);
            if (line.StartsWith("exec -w /etc web sh -c ", StringComparison.Ordinal))
            {
                // A shell inside the container that knows one command.
                var output = line.EndsWith("sh -c cat motd", StringComparison.Ordinal) ? Motd + "\n" : string.Empty;
                await destination.WriteAsync(System.Text.Encoding.UTF8.GetBytes(output), cancellationToken);
                return new Core.Services.Processes.ProcessOutput(output.Length > 0 ? 0 : 127, string.Empty, output.Length > 0 ? string.Empty : "sh: not found");
            }

            if (line != "cp web:/etc/motd -")
            {
                return new Core.Services.Processes.ProcessOutput(1, string.Empty, "unexpected");
            }

            using var tar = new MemoryStream();
            using (var writer = new System.Formats.Tar.TarWriter(tar, System.Formats.Tar.TarEntryFormat.Pax, leaveOpen: true))
            {
                writer.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "motd")
                {
                    DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Motd)),
                });
            }

            await destination.WriteAsync(tar.ToArray(), cancellationToken);
            return new Core.Services.Processes.ProcessOutput(0, string.Empty, string.Empty);
        }
    }

    /// <summary>
    /// Ctrl+O's shell without a shell: it borrows the app's terminal through the real
    /// <see cref="iConsole.Services.ConsoleOwnership"/> — so the renderer and the input loop really
    /// stand still and really take the terminal back — writes a prompt onto the virtual screen as a
    /// shell would, and "exits" when the test says so.
    /// </summary>
    public sealed class Shell(iConsole.Services.ConsoleOwnership consoleOwnership, iConsole.Drivers.IConsoleDriver consoleDriver)
        : Core.Services.Shell.IInteractiveShellService
    {
        public const string Prompt = "sandbox-shell$ ";

        private TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _running;

        /// <summary>What the last Ctrl+O asked to run.</summary>
        public Core.Services.Shell.ShellLaunch? LastLaunch { get; private set; }

        public bool IsRunning => Volatile.Read(ref _running) != 0;

        /// <summary>Ends the running shell, as typing <c>exit</c> would.</summary>
        public void Exit() => _exit.TrySetResult();

        public async Task<Core.Services.Shell.ShellOutcome> RunAsync(Core.Services.Shell.ShellLaunch launch, CancellationToken cancellationToken = default)
        {
            LastLaunch = launch;
            _exit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _running, 1);
            try
            {
                return await consoleOwnership.LendAsync(() =>
                {
                    consoleDriver.Write(0, 0, new iConsole.Display.ConxelString(Prompt + launch.Location, iConsole.Display.Color.White, iConsole.Display.Color.Black));
                    _exit.Task.Wait(TimeSpan.FromSeconds(30));
                    return new Core.Services.Shell.ShellOutcome(0, null);
                }, cancellationToken);
            }
            finally
            {
                Volatile.Write(ref _running, 0);
            }
        }
    }

    /// <summary>
    /// Starts nothing: records which program was asked to open which file. A test that pressed Enter
    /// on a file would otherwise start a real program on the machine running the suite.
    /// </summary>
    public sealed class Launcher : Core.Services.FileAssociations.IFileLauncher
    {
        private readonly Lock _lock = new();
        private readonly List<(Core.Services.FileAssociations.FileAssociation? Association, string FilePath)> _launches = [];

        /// <summary>What the next launch answers with; null for success.</summary>
        public string? NextFailure { get; set; }

        /// <summary>Every launch so far, in order; a null association is the system default.</summary>
        public IReadOnlyList<(Core.Services.FileAssociations.FileAssociation? Association, string FilePath)> Launches
        {
            get
            {
                lock (_lock)
                {
                    return [.. _launches];
                }
            }
        }

        public Task<Core.Services.FileAssociations.LaunchOutcome> LaunchAsync(
            Core.Services.FileAssociations.FileAssociation association, string filePath, CancellationToken cancellationToken = default) =>
            Record(association, filePath);

        public Task<Core.Services.FileAssociations.LaunchOutcome> OpenWithSystemDefaultAsync(string filePath, CancellationToken cancellationToken = default) =>
            Record(null, filePath);

        private Task<Core.Services.FileAssociations.LaunchOutcome> Record(Core.Services.FileAssociations.FileAssociation? association, string filePath)
        {
            lock (_lock)
            {
                _launches.Add((association, filePath));
            }

            var failure = NextFailure;
            NextFailure = null;
            return Task.FromResult(failure is null
                ? Core.Services.FileAssociations.LaunchOutcome.Success
                : Core.Services.FileAssociations.LaunchOutcome.Failed(failure));
        }
    }

    /// <summary>Every symbol trades at 123.45.</summary>
    public sealed class Quotes : IQuoteProvider
    {
        public Task<IReadOnlyDictionary<string, StooqQuote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, StooqQuote>>(
                symbols.ToDictionary(s => s, s => new StooqQuote(s, 123.45m, DateTime.Now)));
    }
}

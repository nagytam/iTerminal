using System.Text;
using Microsoft.Mxc.Sdk.V1;

namespace iTerminal.Core.Services.Shell;

/// <summary>
/// Runs the shell in a Windows ProcessContainer through the Microsoft Execution Containers SDK
/// (<c>Microsoft.Mxc.Sdk</c>, a preview). The session's folder is writable, the process's working directory is the
/// folder, and the sandbox's UI policy is the SDK default (no UI, no clipboard).
///
/// <para>Unlike <see cref="BubblewrapSandbox"/>, this sandbox creates the process itself, so <see cref="Wrap"/>
/// refuses: a command that could not be confined is never run unconfined.</para>
/// </summary>
public sealed class ProcessContainerSandbox : ISessionSandbox
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Lazy<bool> _hasProcessContainer = new(ProbeProcessContainer);

    public string Name => "process-container";

    /// <summary>Windows only, and only where the SDK's native library loads and reports a ProcessContainer backend.</summary>
    public bool IsAvailable => OperatingSystem.IsWindows() && _hasProcessContainer.Value;

    public SessionLaunch Wrap(SessionLaunch shell, string folder) =>
        throw new NotSupportedException("The process-container sandbox starts the shell itself; use Start.");

    public IShellProcess? Start(SessionLaunch shell, string folder)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("The process-container sandbox is not available on this machine.");
        }

        try
        {
            return new MxcShellProcess(MxcContainer.Spawn(BuildRequest(shell, folder)));
        }
        catch (MxcException error)
        {
            // The error says what failed. The shell is not started unconfined in its place.
            throw new InvalidOperationException($"The process-container sandbox could not start the shell: {error.Message}", error);
        }
    }

    /// <summary>
    /// The one-command request for <paramref name="shell"/> in <paramref name="folder"/>. Environment and network are
    /// left at the SDK defaults: the backend's environment, and whatever the backend's network default is.
    /// </summary>
    internal static ContainerRequest BuildRequest(SessionLaunch shell, string folder)
    {
        var exists = Directory.Exists(folder);
        var request = new ContainerRequest(CommandLine(shell))
        {
            Containment = new Containment.ProcessContainer(),
            WorkingDirectory = exists ? folder : null,
        };

        if (exists)
        {
            request.Filesystem = new FilesystemPolicy();
            request.Filesystem.ReadwritePaths.Add(folder);
        }

        return request;
    }

    /// <summary>The command line the SDK takes: the program and its arguments, quoted where they contain spaces or quotes.</summary>
    internal static string CommandLine(SessionLaunch shell) =>
        string.Join(' ', new[] { shell.Program }.Concat(shell.Arguments).Select(Quote));

    private static string Quote(string argument) =>
        argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0
            ? argument
            : "\"" + argument.Replace("\"", "\\\"") + "\"";

    private static bool ProbeProcessContainer()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return MxcPlatform.GetAvailableBackends().Any(backend => backend.Backend == ContainmentBackend.ProcessContainer);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or MxcException)
        {
            // The native library is missing or cannot be loaded on this machine.
            return false;
        }
    }

    /// <summary>The SDK's process as the session reads it: its stdio as text, and its exit.</summary>
    private sealed class MxcShellProcess(MxcProcess process) : IShellProcess
    {
        private readonly StreamWriter _input = new(process.StandardInput ?? throw new InvalidOperationException("The sandboxed shell has no stdin."), Utf8);
        private readonly StreamReader _output = new(process.StandardOutput ?? throw new InvalidOperationException("The sandboxed shell has no stdout."), Utf8);
        private readonly StreamReader _error = new(process.StandardError ?? throw new InvalidOperationException("The sandboxed shell has no stderr."), Utf8);

        public bool HasExited => process.TryGetExitCode(out _);

        public TextWriter StandardInput => _input;

        public TextReader StandardOutput => _output;

        public TextReader StandardError => _error;

        public void Kill()
        {
            try
            {
                process.Kill();
            }
            catch (MxcException)
            {
                // Already gone.
            }
        }

        public void WaitForExit(int milliseconds)
        {
            using var timeout = new CancellationTokenSource(milliseconds);
            try
            {
                process.WaitAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // Still running after the wait.
            }
            catch (MxcException)
            {
                // No terminal result; the caller disposes the process either way.
            }
        }

        public void Dispose()
        {
            _input.Dispose();
            _output.Dispose();
            _error.Dispose();
            process.Dispose();
        }
    }
}

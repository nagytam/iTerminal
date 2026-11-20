namespace iTerminal.Core.Services.Shell;

/// <summary>
/// Owns the command log's shell session (see <see cref="ILocalShellSessionService"/>). The selection is saved in
/// <see cref="ShellKinds.SettingKey"/>; the settings service has no change notification, so it is read once, on
/// first use, and written back through <see cref="ISettingsService.Set"/>.
///
/// <para>Folder rule: a session starts in the folder the command log runs in. After that its own working directory
/// persists (a <c>cd</c> typed in it stays). Before a command the session changes to the panel's folder only when
/// the panel has moved since the last command; after a command the panel is told the session's folder, and the
/// caller moves the panel only while it still shows the folder the command started from.</para>
/// </summary>
public sealed class LocalShellSessionService(ISettingsService settings) : ILocalShellSessionService
{
    private readonly Lock _gate = new();
    private readonly Lazy<IReadOnlyList<ShellKind>> _available = new(FindAvailable);
    private LocalShellSession? _session;
    private ShellKind? _selected;
    private string? _panelFolder;
    private int _busy;

    public IReadOnlyList<ShellKind> Available() => _available.Value;

    public bool IsAvailable => _available.Value.Count > 0;

    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    public ShellKind Selected
    {
        get
        {
            lock (_gate)
            {
                return _selected ??= Initial(settings.Get(ShellKinds.SettingKey), _available.Value);
            }
        }
    }

    public bool Switch(ShellKind kind)
    {
        if (IsBusy || !_available.Value.Contains(kind))
        {
            return false;
        }

        lock (_gate)
        {
            if (_selected == kind)
            {
                return true;
            }

            _session?.Dispose();
            _session = null;
            _panelFolder = null;
            _selected = kind;
            settings.Set(ShellKinds.SettingKey, ShellKinds.Name(kind));
        }

        return true;
    }

    public async Task<ShellCommandResult> RunAsync(string folder, string command, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("No shell is available to run the command in.");
        }

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw new InvalidOperationException("A command is already running in the session.");
        }

        try
        {
            var session = Ensure(folder);
            if (_panelFolder is not null && !SameFolder(_panelFolder, folder) && session.Folder != folder)
            {
                var change = await session.RunAsync(ShellKinds.ChangeDirectory(session.Kind, folder), cancellationToken);
                if (change.State != ShellRunState.Completed || change.ExitCode != 0)
                {
                    return change with { Error = $"cd: {folder}: could not change folder. " + change.Error };
                }
            }

            _panelFolder = folder;
            var result = await session.RunAsync(command, cancellationToken);
            if (result.State == ShellRunState.Completed)
            {
                _panelFolder = result.Folder;
            }
            else
            {
                DropSession(session);
            }

            return result;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    public void Interrupt()
    {
        LocalShellSession? session;
        lock (_gate)
        {
            session = _session;
        }

        session?.Interrupt();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _session?.Dispose();
            _session = null;
        }
    }

    /// <summary>The live session, or a new one started in <paramref name="folder"/> when there is none or it has ended.</summary>
    private LocalShellSession Ensure(string folder)
    {
        lock (_gate)
        {
            if (_session is { HasEnded: false } live)
            {
                return live;
            }

            _session?.Dispose();
            var kind = _selected ??= Initial(settings.Get(ShellKinds.SettingKey), _available.Value);
            var program = ShellLaunch.FindOnPath(ShellKinds.Name(kind)) ?? ShellKinds.Name(kind);
            _session = LocalShellSession.Start(kind, program, folder);
            _panelFolder = null;
            return _session;
        }
    }

    private void DropSession(LocalShellSession session)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_session, session))
            {
                _session.Dispose();
                _session = null;
                _panelFolder = null;
            }
        }
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.Ordinal);

    /// <summary>The setting when it names an available kind; else the basename of <c>$SHELL</c> when it does; else sh.</summary>
    internal static ShellKind Initial(string? setting, IReadOnlyList<ShellKind> available)
    {
        if (ShellKinds.TryParse(setting, out var fromSetting) && available.Contains(fromSetting))
        {
            return fromSetting;
        }

        var shell = Path.GetFileName(Environment.GetEnvironmentVariable("SHELL") ?? string.Empty);
        if (ShellKinds.TryParse(shell, out var fromEnvironment) && available.Contains(fromEnvironment))
        {
            return fromEnvironment;
        }

        return available.Contains(ShellKind.Sh) ? ShellKind.Sh : available.FirstOrDefault(ShellKind.Sh);
    }

    private static IReadOnlyList<ShellKind> FindAvailable()
    {
        if (OperatingSystem.IsWindows())
        {
            // sh and bash on Windows are WSL or Git Bash, whose paths do not map back to the panels; not in this version.
            return [];
        }

        return ShellKinds.All.Where(kind => ShellLaunch.FindOnPath(ShellKinds.Name(kind)) is not null).ToList();
    }
}

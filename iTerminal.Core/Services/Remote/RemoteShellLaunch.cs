using iTerminal.Core.Model.Remote;
using iTerminal.Core.Services.Shell;

namespace iTerminal.Core.Services.Remote;

/// <summary>
/// Turns a saved <see cref="RemoteConnection"/> into the program the terminal is lent to
/// (<see cref="IInteractiveShellService"/>): the system's <c>ssh</c>, or PowerShell running
/// <c>Enter-PSSession</c>. Both are given the real terminal, so a password, a passphrase or a host-key
/// question is asked there rather than saved or typed into the app.
/// </summary>
public static class RemoteShellLaunch
{
    /// <summary>The launch for <paramref name="connection"/>, or why there is none on this machine.</summary>
    public static bool TryCreate(RemoteConnection connection, out ShellLaunch? launch, out string? error)
    {
        launch = null;
        error = connection.Validate();
        if (error is not null)
        {
            return false;
        }

        if (connection.IsFileDrive)
        {
            error = "An FTP or WebDAV connection opens as a panel (B), not as a shell.";
            return false;
        }

        if (connection.Protocol == RemoteProtocol.Ssh)
        {
            launch = Ssh(connection);
            return true;
        }

        return TryCreatePowerShell(connection, ShellLaunch.FindOnPath(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh"), out launch, out error);
    }

    /// <summary><c>ssh [-p port] [-i key] [user@]host</c> — no remote command, so the server starts the user's login shell.</summary>
    public static ShellLaunch Ssh(RemoteConnection connection)
    {
        List<string> arguments = [];
        if (connection.Port is { } port)
        {
            arguments.Add("-p");
            arguments.Add(port.ToString());
        }

        if (!string.IsNullOrWhiteSpace(connection.PrivateKeyPath))
        {
            arguments.Add("-i");
            arguments.Add(connection.PrivateKeyPath.Trim());
        }

        arguments.Add(connection.Destination);
        return new ShellLaunch("ssh", arguments, WorkingDirectory: null, $"{connection.Name} ({connection.Destination})");
    }

    /// <param name="pwshPath">PowerShell 7 when it is installed; the fallback on Windows is the built-in Windows PowerShell.</param>
    internal static bool TryCreatePowerShell(RemoteConnection connection, string? pwshPath, out ShellLaunch? launch, out string? error)
    {
        launch = null;
        error = null;

        string program;
        if (connection.Transport == PowerShellTransport.Ssh)
        {
            if (pwshPath is null)
            {
                error = "PowerShell over SSH needs PowerShell 7 (pwsh) on this machine.";
                return false;
            }

            program = pwshPath;
        }
        else
        {
            if (!OperatingSystem.IsWindows())
            {
                error = "PowerShell over WinRM runs from Windows; use the SSH transport from here.";
                return false;
            }

            program = pwshPath ?? "powershell.exe";
        }

        // -Command with no -NoExit: leaving the remote session (exit) ends the process and returns here.
        launch = new ShellLaunch(program, ["-NoLogo", "-NoProfile", "-Command", EnterSessionCommand(connection)], WorkingDirectory: null,
            $"{connection.Name} ({connection.Destination}, PowerShell)");
        return true;
    }

    /// <summary>
    /// The <c>Enter-PSSession</c> call. A WinRM user is passed as <c>-Credential 'name'</c>, which makes
    /// PowerShell ask for the password itself; with none, the current identity is used.
    /// </summary>
    internal static string EnterSessionCommand(RemoteConnection connection)
    {
        List<string> parts = ["Enter-PSSession"];
        if (connection.Transport == PowerShellTransport.Ssh)
        {
            parts.Add("-HostName " + QuotePowerShell(connection.Host));
            if (!string.IsNullOrEmpty(connection.Username))
            {
                parts.Add("-UserName " + QuotePowerShell(connection.Username));
            }

            if (connection.Port is { } port)
            {
                parts.Add("-Port " + port);
            }

            if (!string.IsNullOrWhiteSpace(connection.PrivateKeyPath))
            {
                parts.Add("-KeyFilePath " + QuotePowerShell(connection.PrivateKeyPath.Trim()));
            }
        }
        else
        {
            parts.Add("-ComputerName " + QuotePowerShell(connection.Host));
            if (connection.Port is { } port)
            {
                parts.Add("-Port " + port);
            }

            if (!string.IsNullOrEmpty(connection.Username))
            {
                parts.Add("-Credential " + QuotePowerShell(connection.Username));
            }
        }

        return string.Join(' ', parts);
    }

    /// <summary>A single-quoted PowerShell string: everything literal, a quote doubled.</summary>
    internal static string QuotePowerShell(string? text) => "'" + (text ?? string.Empty).Replace("'", "''") + "'";
}

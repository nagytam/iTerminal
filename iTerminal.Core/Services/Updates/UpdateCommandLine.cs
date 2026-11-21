namespace iTerminal.Core.Services.Updates;

/// <summary>
/// The <c>--update</c> command line switch: checks for a newer release, installs it without asking, and returns, so
/// that the application runs no interface. The installer or update script takes over from there, and the caller
/// exits, which lets the script replace the files (on Linux, and on Windows where the MSI or portable script needs them).
/// </summary>
public static class UpdateCommandLine
{
    public const string Argument = "--update";

    /// <summary>Exit code: nothing needed installing, or the update was started.</summary>
    public const int Success = 0;

    /// <summary>Exit code: a release was found but could not be installed by this copy.</summary>
    public const int Failed = 1;

    /// <summary>
    /// Installs the newest release, reporting each step to <paramref name="output"/>. A newer release that is only
    /// announced (a Linux package) is a failure, because nothing was updated. Skipping a release does not stop it:
    /// asking for the update is the answer to it.
    /// </summary>
    public static async Task<int> RunAsync(IUpdateService updates, TextWriter output, CancellationToken cancellationToken)
    {
        try
        {
            return await RunCoreAsync(updates, output, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelled (Ctrl+C): nothing is reported as installed, and the download is left as a .partial file.
            output.WriteLine("The update was cancelled. Nothing was installed.");
            return Failed;
        }
    }

    private static async Task<int> RunCoreAsync(IUpdateService updates, TextWriter output, CancellationToken cancellationToken)
    {
        output.WriteLine($"iTerminal {updates.CurrentVersion}: looking for a newer release...");
        UpdateInfo? update = await updates.CheckAsync(cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            output.WriteLine("The update was cancelled. Nothing was installed.");
            return Failed;
        }

        if (update is null)
        {
            output.WriteLine("iTerminal is up to date, or GitHub could not be reached.");
            return Success;
        }

        if (updates.InstallKind == UpdateInstallKind.PackageManager)
        {
            output.WriteLine($"iTerminal {update.Version} is available. WinGet installed this copy and updates it: run winget upgrade {UpdateService.WinGetPackageId}");
            return Failed;
        }

        if (!updates.CanInstall)
        {
            output.WriteLine($"iTerminal {update.Version} is available, but this copy cannot update itself. Download it from {updates.ReleasesAddress}");
            return Failed;
        }

        output.WriteLine($"Installing iTerminal {update.Version}...");
        bool started = await updates.InstallAsync(cancellationToken);
        foreach (string line in updates.Log)
        {
            output.WriteLine(line);
        }

        return started ? Success : Failed;
    }
}

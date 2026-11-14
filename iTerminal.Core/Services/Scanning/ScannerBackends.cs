using iTerminal.Core.Services.Processes;
using iTerminal.Core.Services.Scanning.Sane;
using iTerminal.Core.Services.Scanning.Twain;

namespace iTerminal.Core.Services.Scanning;

/// <summary>Picks the scanner backend for the platform: TWAIN on Windows, SANE everywhere else.</summary>
public static class ScannerBackends
{
    public static IScannerBackend Create(IExternalProcessRunner processRunner) =>
        OperatingSystem.IsWindows()
            ? new TwainScannerBackend()
            : new SaneScannerBackend(processRunner);
}

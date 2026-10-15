using iConsole.Buffers;
using iConsole.Drivers;

namespace iConsole.Helper;

/// <summary>
/// No-op implementation of <see cref="IRenderingDumpHelper"/> used when dumping is
/// disabled or when no real helper has been injected (e.g. in unit tests).
/// All methods are thread-safe and allocation-free.
/// </summary>
public sealed class NullRenderingDumpHelper : IRenderingDumpHelper
{
    public static readonly NullRenderingDumpHelper Instance = new();

    public bool   IsEnabled        => false;
    public string DumpDirectoryPath => string.Empty;

    public void InitializeSession(IConsoleDriver consoleDriver) { }
    public void AppendSessionTrace(string message)              { }
    public void DumpBufferStage(string stageName, BufferLayer layer, IReadOnlyCollection<string>? details = null) { }
    public void DumpTextArtifact(string fileName, string content) { }
    public void AppendError(Exception exception)               { }
}

using iConsole.Buffers;
using iConsole.Drivers;

namespace iConsole.Helper;

/// <summary>
/// Abstracts rendering-dump I/O so that each <see cref="iConsole.Jobs.ConsoleBufferRendererJob"/>
/// instance owns its own session state and tests can run fully in parallel without sharing
/// any static mutable fields.
/// </summary>
public interface IRenderingDumpHelper
{
    bool IsEnabled { get; }
    string DumpDirectoryPath { get; }

    void InitializeSession(IConsoleDriver consoleDriver);
    void AppendSessionTrace(string message);
    void DumpBufferStage(string stageName, BufferLayer layer, IReadOnlyCollection<string>? details = null);
    void DumpTextArtifact(string fileName, string content);
    void AppendError(Exception exception);
}

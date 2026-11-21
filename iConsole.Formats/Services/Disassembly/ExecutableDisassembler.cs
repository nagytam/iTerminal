using iConsole.Executables;

namespace iConsole.Formats.Services.Disassembly;

/// <summary>The executable analysis view's disassembler: <see cref="ExecutableDisassembly.Build"/> behind the interface.</summary>
public sealed class ExecutableDisassembler : IExecutableDisassembler
{
    public IReadOnlyList<string> Build(string path, CancellationToken cancellationToken) =>
        ExecutableDisassembly.Build(path, cancellationToken);
}

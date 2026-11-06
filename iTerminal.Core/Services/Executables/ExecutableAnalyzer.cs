using iConsole.Executables;

namespace iTerminal.Core.Services.Executables;

/// <summary>
/// Reads the headers of a PE, ELF or Mach-O file and reports them. The file is only parsed, never run or loaded. Every offset and count
/// in a file is distrusted: reads are bounds-checked, tables are capped, and a file that cannot be parsed raises
/// <see cref="ExecutableAnalysisException"/> and nothing else.
/// </summary>
public sealed class ExecutableAnalyzer : IExecutableAnalyzer
{
    public ExecutableReport Analyze(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var file = new BoundedFileReader(path);
            byte[] start = new byte[4];
            int length = (int)Math.Min(start.Length, file.Length);
            if (length < 2 || !file.TryRead(0, start.AsSpan(0, length)))
            {
                throw new ExecutableAnalysisException("The file is too short to be an executable.");
            }

            if (start[0] == 'M' && start[1] == 'Z')
            {
                return PeAnalyzer.Analyze(path, file, cancellationToken);
            }

            if (length == 4 && start[0] == 0x7F && start[1] == 'E' && start[2] == 'L' && start[3] == 'F')
            {
                return ElfAnalyzer.Analyze(file, cancellationToken);
            }

            if (length == 4 && ExecutableFileTypes.HasSignature(ReadHeader(file)))
            {
                return MachOAnalyzer.Analyze(file, cancellationToken);
            }

            throw new ExecutableAnalysisException("The file is not a PE, ELF or Mach-O executable.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException
            or InvalidOperationException or OverflowException or BadImageFormatException or NotSupportedException)
        {
            // A parser bug on a hostile file must not take the application down: the reader checks the offsets, this is the net.
            throw new ExecutableAnalysisException(ex.Message, ex);
        }
    }

    private static byte[] ReadHeader(BoundedFileReader file)
    {
        byte[] bytes = new byte[ExecutableFileTypes.HeaderLength];
        int length = (int)Math.Min(bytes.Length, file.Length);
        file.TryRead(0, bytes.AsSpan(0, length));
        return bytes[..length];
    }
}

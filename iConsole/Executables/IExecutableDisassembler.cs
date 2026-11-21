namespace iConsole.Executables;

/// <summary>
/// Builds the disassembly listing of a local executable for the executable analysis view. The disassembler is in
/// <c>iConsole.Formats</c>, which implements this interface, so the dialog here never references it.
/// </summary>
public interface IExecutableDisassembler
{
    /// <summary>Blocking file I/O: the caller runs it on a worker thread.</summary>
    IReadOnlyList<string> Build(string path, CancellationToken cancellationToken);
}

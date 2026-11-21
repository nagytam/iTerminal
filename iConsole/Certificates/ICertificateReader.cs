namespace iConsole.Certificates;

/// <summary>
/// Reads certificates from bytes or from a file. The implementation lives in <c>iConsole.Formats</c>, so the viewer dialogs
/// here never reference the X.509 code.
/// </summary>
public interface ICertificateReader
{
    CertificateReadResult Read(ReadOnlyMemory<byte> bytes);

    CertificateFileRead ReadFile(string path);
}

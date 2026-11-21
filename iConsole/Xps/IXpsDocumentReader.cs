namespace iConsole.Xps;

/// <summary>
/// Reads an XPS or OpenXPS package into its pages. The implementation is in <c>iConsole.Formats</c>, so the XPS viewer here
/// never references the package reader.
/// </summary>
public interface IXpsDocumentReader
{
    /// <summary>Reads an XPS or OpenXPS package. Never throws: a failure is returned in <see cref="XpsDocument.Error"/>.</summary>
    XpsDocument Read(byte[] content);
}

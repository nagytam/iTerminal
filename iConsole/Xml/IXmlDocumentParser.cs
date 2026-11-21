using System.Xml.Linq;

namespace iConsole.Xml;

/// <summary>
/// Parses an XML document into the element tree the viewer shows. The implementation is in <c>iConsole.Formats</c>.
/// </summary>
public interface IXmlDocumentParser
{
    /// <summary>Parses a document, or throws an <see cref="System.Xml.XmlException"/> that says where it is malformed. Elements keep their line numbers.</summary>
    XDocument Parse(byte[] content);
}

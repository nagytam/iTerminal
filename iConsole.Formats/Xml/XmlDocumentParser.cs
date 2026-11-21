using System.Xml.Linq;
using iConsole.Xml;

namespace iConsole.Formats.Xml;

/// <summary>Parses XML bytes into an <see cref="XDocument"/> with line numbers. Lives in <c>iConsole.Formats</c>.</summary>
public sealed class XmlDocumentParser : IXmlDocumentParser
{
    /// <inheritdoc/>
    public XDocument Parse(byte[] content) => XDocument.Load(new MemoryStream(content), LoadOptions.SetLineInfo);
}

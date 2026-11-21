namespace iConsole.Xml;

/// <summary>
/// Validates an XML document against the schema or DTD it names. The implementation is in <c>iConsole.Formats</c>, so the XML
/// viewer here never references the validator's XML reader settings.
/// </summary>
public interface IXmlValidator
{
    /// <summary>The issues found, errors and warnings both; an empty list means the document is valid.</summary>
    /// <param name="content">The document's bytes.</param>
    /// <param name="filePath">The document's local path, which relative schema locations are resolved against; null when it is not local.</param>
    IReadOnlyList<XmlValidationIssue> Validate(byte[] content, string? filePath);
}

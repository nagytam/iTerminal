using System.Xml;
using System.Xml.Schema;

namespace iConsole.Xml;

/// <summary>One problem a validation found: where it is, whether it is an error or a warning, and what it says.</summary>
public sealed record XmlValidationIssue(int Line, int Column, bool IsError, string Message);

/// <summary>
/// Validates an XML document against the schema or DTD it names: a DOCTYPE is checked against its DTD, and the
/// <c>xsi:schemaLocation</c> and <c>xsi:noNamespaceSchemaLocation</c> attributes name the XSD files. Schema and DTD files
/// are read from the local disk only, relative to the document, so a document is never validated against something
/// fetched over the network. A document that is not well-formed reports where it breaks.
/// </summary>
public static class XmlValidator
{
    private const string SchemaInstanceNamespace = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>The issues found, errors and warnings both; an empty list means the document is valid.</summary>
    /// <param name="content">The document's bytes.</param>
    /// <param name="filePath">The document's local path, which relative schema locations are resolved against; null when it is not local.</param>
    public static IReadOnlyList<XmlValidationIssue> Validate(byte[] content, string? filePath)
    {
        string? baseUri = filePath is not null && File.Exists(filePath) ? new Uri(Path.GetFullPath(filePath)).AbsoluteUri : null;
        (bool hasDoctype, bool hasSchemaLocation, List<XmlValidationIssue> issues) = Probe(content, baseUri);
        if (issues.Count > 0)
        {
            // Not well-formed: there is no point validating it.
            return issues;
        }

        if (!hasDoctype && !hasSchemaLocation)
        {
            return [new XmlValidationIssue(0, 0, false,
                "The document names no schema or DTD, so there is nothing to validate it against. Add an xsi:schemaLocation or a DOCTYPE.")];
        }

        return Check(content, baseUri, hasDoctype ? ValidationType.DTD : ValidationType.Schema);
    }

    /// <summary>Reads the document without validating it, to find out whether it names a DTD or a schema, or is malformed.</summary>
    private static (bool HasDoctype, bool HasSchemaLocation, List<XmlValidationIssue> Issues) Probe(byte[] content, string? baseUri)
    {
        List<XmlValidationIssue> issues = [];
        bool hasDoctype = false;
        bool hasSchemaLocation = false;
        XmlReaderSettings settings = new() { ValidationType = ValidationType.None, DtdProcessing = DtdProcessing.Parse, XmlResolver = null };
        try
        {
            using XmlReader reader = XmlReader.Create(new MemoryStream(content), settings, baseUri);
            while (reader.Read())
            {
                hasDoctype |= reader.NodeType == XmlNodeType.DocumentType;
                hasSchemaLocation |= reader.NodeType == XmlNodeType.Element && HasSchemaLocation(reader);
            }
        }
        catch (XmlException exception)
        {
            issues.Add(new XmlValidationIssue(exception.LineNumber, exception.LinePosition, true, exception.Message));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            issues.Add(new XmlValidationIssue(0, 0, true, exception.Message));
        }

        return (hasDoctype, hasSchemaLocation, issues);
    }

    /// <summary>Validates against the DTD or the schemas, collecting every error and warning the validator reports.</summary>
    private static IReadOnlyList<XmlValidationIssue> Check(byte[] content, string? baseUri, ValidationType validation)
    {
        List<XmlValidationIssue> issues = [];
        XmlReaderSettings settings = new()
        {
            ValidationType = validation,
            ValidationFlags = XmlSchemaValidationFlags.ProcessIdentityConstraints
                | XmlSchemaValidationFlags.ProcessInlineSchema
                | XmlSchemaValidationFlags.ProcessSchemaLocation
                | XmlSchemaValidationFlags.ReportValidationWarnings,
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = new LocalFilesResolver(),
        };
        settings.ValidationEventHandler += (_, args) => issues.Add(new XmlValidationIssue(
            args.Exception?.LineNumber ?? 0,
            args.Exception?.LinePosition ?? 0,
            args.Severity == XmlSeverityType.Error,
            args.Message));

        try
        {
            using XmlReader reader = XmlReader.Create(new MemoryStream(content), settings, baseUri);
            while (reader.Read())
            {
            }
        }
        catch (XmlException exception)
        {
            issues.Add(new XmlValidationIssue(exception.LineNumber, exception.LinePosition, true, exception.Message));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            issues.Add(new XmlValidationIssue(0, 0, true, exception.Message));
        }

        return issues;
    }

    private static bool HasSchemaLocation(XmlReader reader) =>
        reader.GetAttribute("schemaLocation", SchemaInstanceNamespace) is not null
        || reader.GetAttribute("noNamespaceSchemaLocation", SchemaInstanceNamespace) is not null;

    /// <summary>Opens the schema and DTD files a document refers to, from the local disk only; anything else is an error.</summary>
    private sealed class LocalFilesResolver : XmlResolver
    {
        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            if (!absoluteUri.IsFile)
            {
                throw new XmlException($"Only local schema files are read; {absoluteUri} was not loaded.");
            }

            return File.OpenRead(absoluteUri.LocalPath);
        }
    }
}

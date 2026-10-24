namespace iConsole.Xml;

/// <summary>The extensions F3 opens in the XML viewer. A solution (.sln, .slnx) keeps its own viewer.</summary>
public static class XmlFileTypes
{
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".xml", ".xaml", ".xsd", ".xsl", ".xslt", ".config", ".csproj", ".props", ".targets",
            ".nuspec", ".resx", ".svg", ".rss", ".atom", ".plist",
        };

    public static bool IsXmlFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));
}

using System.Globalization;
using System.Xml.Linq;
using iTerminal.DevelopmentStudio.Model;

namespace iTerminal.DevelopmentStudio.Services.Analysis;

/// <summary>
/// Reads Cobertura XML, the format coverlet (<c>dotnet test --collect:"XPlat Code Coverage"</c>) and coverage.py
/// (<c>coverage xml</c>) both write. A class's <c>filename</c> is resolved against the <c>source</c> folders the report
/// names, and a file found in none of them keeps its relative name.
/// </summary>
public static class CoberturaParser
{
    /// <summary>The coverage in <paramref name="xml"/>. Throws <see cref="System.Xml.XmlException"/> when the text is not XML.</summary>
    public static CoverageReport Parse(string xml)
    {
        XElement root = XDocument.Parse(xml).Root ?? throw new System.Xml.XmlException("No root element.");
        var sources = root.Descendants("source").Select(s => s.Value.Trim()).Where(s => s.Length > 0).ToList();

        // A file can appear in several classes (a partial class, or one file per test run); its hits are summed per line.
        var byPath = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement classElement in root.Descendants("class"))
        {
            string filename = classElement.Attribute("filename")?.Value ?? string.Empty;
            if (filename.Length == 0) continue;
            string path = ResolvePath(filename, sources);
            if (!byPath.TryGetValue(path, out var hits))
            {
                hits = [];
                byPath[path] = hits;
            }

            foreach (XElement line in classElement.Descendants("line"))
            {
                if (!int.TryParse(line.Attribute("number")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) continue;
                int count = int.TryParse(line.Attribute("hits")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) ? h : 0;
                hits[number] = hits.GetValueOrDefault(number) + count;
            }
        }

        return new CoverageReport(byPath
            .Select(pair => new CoverageFile(pair.Key, pair.Value))
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    private static string ResolvePath(string filename, IReadOnlyList<string> sources)
    {
        if (Path.IsPathRooted(filename)) return filename;
        foreach (string source in sources)
        {
            string candidate = Path.GetFullPath(Path.Combine(source, filename));
            if (File.Exists(candidate)) return candidate;
        }

        return sources.Count > 0 ? Path.GetFullPath(Path.Combine(sources[0], filename)) : filename;
    }
}

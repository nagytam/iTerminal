using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using iConsole.Services;

namespace iConsole.Cad;

/// <summary>
/// The summary the F3 viewer shows above a drawing or a 3D model: what the file holds, read from its bytes without
/// loading, drawing or running it. Covers AutoCAD DXF (ASCII and binary), Wavefront OBJ and STL (ASCII and binary).
/// A text format keeps its source below the summary, as before; a binary one shows the summary alone.
/// </summary>
public static class ModelFileSummary
{
    /// <summary>The extensions the viewer summarizes.</summary>
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dxf", "obj", "stl" };

    private static readonly byte[] BinaryDxfSignature = "AutoCAD Binary DXF\r\n\u001a\0"u8.ToArray();

    private const int StlHeaderLength = 84;
    private const int StlTriangleLength = 50;

    /// <summary>The lines to show, and whether the file's own text follows them.</summary>
    public sealed record Result(IReadOnlyList<string> Lines, bool ShowSource);

    /// <exception cref="FormatException">The bytes are not the format the extension names; the caller shows the text instead.</exception>
    public static Result Summarize(string extension, byte[] content) => extension.ToLowerInvariant() switch
    {
        "dxf" => SummarizeDxf(content),
        "obj" => SummarizeObj(content),
        "stl" => SummarizeStl(content),
        _ => throw new FormatException($"No summary for .{extension}."),
    };

    private static string Label(string key, string fallback) => ViewerText.Get("Model." + key, fallback);

    private static string Number(double value) => value.ToString("G6", CultureInfo.InvariantCulture);

    private static string Bounds(double[] min, double[] max) =>
        $"({Number(min[0])}, {Number(min[1])}, {Number(min[2])}) – ({Number(max[0])}, {Number(max[1])}, {Number(max[2])})";

    #region DXF

    private static Result SummarizeDxf(byte[] content)
    {
        var lines = new List<string> { Label("Heading", "Model summary") };
        if (content.Length >= BinaryDxfSignature.Length && content.AsSpan(0, BinaryDxfSignature.Length).SequenceEqual(BinaryDxfSignature))
        {
            // Binary DXF packs the group codes and values differently; the summary says what it cannot list.
            lines.Add(Label("BinaryDxf", "This is a binary DXF file, so its entities are not listed."));
            return new Result(lines, ShowSource: false);
        }

        var pairs = ReadDxfPairs(Encoding.UTF8.GetString(content));
        string section = string.Empty;
        string variable = string.Empty;
        string? version = null;
        var extents = new Dictionary<string, double[]>(StringComparer.Ordinal);
        var layers = new List<string>();
        var entities = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < pairs.Count; i++)
        {
            (int code, string value) = pairs[i];
            switch (code)
            {
                case 0 when value == "SECTION":
                    section = i + 1 < pairs.Count && pairs[i + 1].Code == 2 ? pairs[i + 1].Value : string.Empty;
                    i++;
                    break;
                case 0 when value == "ENDSEC":
                    section = string.Empty;
                    break;
                case 0 when section == "ENTITIES" && value != "EOF":
                    entities[value] = entities.GetValueOrDefault(value) + 1;
                    break;
                case 0 when section == "TABLES" && value == "LAYER":
                    if (i + 1 < pairs.Count && pairs[i + 1].Code == 2)
                    {
                        layers.Add(pairs[i + 1].Value);
                        i++;
                    }
                    break;
                case 9 when section == "HEADER":
                    variable = value;
                    break;
                case 1 when section == "HEADER" && variable == "$ACADVER":
                    version = value;
                    break;
                case 10 when section == "HEADER" && variable is "$EXTMIN" or "$EXTMAX":
                    ExtentOf(extents, variable)[0] = ParseDouble(value);
                    break;
                case 20 when section == "HEADER" && variable is "$EXTMIN" or "$EXTMAX":
                    ExtentOf(extents, variable)[1] = ParseDouble(value);
                    break;
            }
        }

        if (version is null && entities.Count == 0 && layers.Count == 0)
        {
            throw new FormatException("Not a DXF drawing.");
        }

        if (version is not null)
        {
            lines.Add($"{Label("Version", "Version")}: {version}");
        }

        if (extents.ContainsKey("$EXTMIN") && extents.ContainsKey("$EXTMAX"))
        {
            lines.Add($"{Label("Extents", "Extents")}: {Bounds2(extents["$EXTMIN"], extents["$EXTMAX"])}");
        }

        lines.Add($"{Label("Layers", "Layers")}: {layers.Count}{ListSuffix(layers)}");
        lines.Add($"{Label("Entities", "Entities")}: {entities.Values.Sum()}");
        foreach (var (type, count) in entities.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            lines.Add($"  {type}: {count}");
        }

        return new Result(lines, ShowSource: true);
    }

    private static List<(int Code, string Value)> ReadDxfPairs(string text)
    {
        var raw = text.Split('\n');
        var pairs = new List<(int, string)>(raw.Length / 2);
        for (int i = 0; i + 1 < raw.Length; i += 2)
        {
            if (!int.TryParse(raw[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
            {
                throw new FormatException("A DXF group code is not a number.");
            }

            pairs.Add((code, raw[i + 1].Trim()));
        }

        return pairs;
    }

    private static double[] ExtentOf(Dictionary<string, double[]> extents, string variable)
    {
        if (!extents.TryGetValue(variable, out double[]? corner))
        {
            corner = new double[2];
            extents[variable] = corner;
        }

        return corner;
    }

    private static string Bounds2(double[] min, double[] max) =>
        $"({Number(min[0])}, {Number(min[1])}) – ({Number(max[0])}, {Number(max[1])})";

    #endregion

    #region OBJ

    private static Result SummarizeObj(byte[] content)
    {
        int vertices = 0, textures = 0, normals = 0, faces = 0, groups = 0, objects = 0, recognised = 0;
        var materials = new List<string>();
        var libraries = new List<string>();
        var min = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var max = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };

        foreach (string raw in Encoding.UTF8.GetString(content).Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            int space = line.IndexOf(' ');
            string keyword = space < 0 ? line : line[..space];
            string rest = space < 0 ? string.Empty : line[(space + 1)..].Trim();
            switch (keyword)
            {
                case "v":
                    recognised++;
                    vertices++;
                    ExtendBounds(rest, min, max);
                    break;
                case "vt":
                    recognised++;
                    textures++;
                    break;
                case "vn":
                    recognised++;
                    normals++;
                    break;
                case "f":
                    recognised++;
                    faces++;
                    break;
                case "g":
                    recognised++;
                    groups++;
                    break;
                case "o":
                    recognised++;
                    objects++;
                    break;
                case "usemtl":
                    recognised++;
                    if (rest.Length > 0 && !materials.Contains(rest))
                    {
                        materials.Add(rest);
                    }
                    break;
                case "mtllib":
                    recognised++;
                    foreach (string name in rest.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!libraries.Contains(name))
                        {
                            libraries.Add(name);
                        }
                    }
                    break;
            }
        }

        if (recognised == 0)
        {
            throw new FormatException("Not a Wavefront OBJ model.");
        }

        var lines = new List<string>
        {
            Label("Heading", "Model summary"),
            $"{Label("Vertices", "Vertices")}: {vertices}",
            $"{Label("TextureCoordinates", "Texture coordinates")}: {textures}",
            $"{Label("Normals", "Normals")}: {normals}",
            $"{Label("Faces", "Faces")}: {faces}",
        };
        if (groups > 0)
        {
            lines.Add($"{Label("Groups", "Groups")}: {groups}");
        }

        if (objects > 0)
        {
            lines.Add($"{Label("Objects", "Objects")}: {objects}");
        }

        if (materials.Count > 0)
        {
            lines.Add($"{Label("Materials", "Materials")}: {string.Join(", ", materials.Take(20))}{(materials.Count > 20 ? ", …" : string.Empty)}");
        }

        if (libraries.Count > 0)
        {
            lines.Add($"{Label("MaterialLibraries", "Material libraries")}: {string.Join(", ", libraries)}");
        }

        if (vertices > 0)
        {
            lines.Add($"{Label("Bounds", "Bounds")}: {Bounds(min, max)}");
        }

        return new Result(lines, ShowSource: true);
    }

    private static void ExtendBounds(string coordinates, double[] min, double[] max)
    {
        string[] parts = coordinates.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int axis = 0; axis < 3 && axis < parts.Length; axis++)
        {
            double value = ParseDouble(parts[axis]);
            min[axis] = Math.Min(min[axis], value);
            max[axis] = Math.Max(max[axis], value);
        }
    }

    #endregion

    #region STL

    private static Result SummarizeStl(byte[] content)
    {
        var lines = new List<string> { Label("Heading", "Model summary") };
        var min = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var max = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };

        if (IsBinaryStl(content, out uint declared))
        {
            // The header's count is trusted up to the bytes actually present, so a truncated file is summarized, not refused.
            int available = (content.Length - StlHeaderLength) / StlTriangleLength;
            int triangles = (int)Math.Min(declared, (uint)available);
            for (int t = 0; t < triangles; t++)
            {
                int offset = StlHeaderLength + t * StlTriangleLength + 12; // skip the facet normal
                for (int vertex = 0; vertex < 3; vertex++)
                {
                    for (int axis = 0; axis < 3; axis++)
                    {
                        float value = BinaryPrimitives.ReadSingleLittleEndian(content.AsSpan(offset + (vertex * 3 + axis) * 4, 4));
                        min[axis] = Math.Min(min[axis], value);
                        max[axis] = Math.Max(max[axis], value);
                    }
                }
            }

            lines.Add($"{Label("Format", "Format")}: {Label("Binary", "binary")}");
            lines.Add($"{Label("Triangles", "Triangles")}: {triangles}");
            if (triangles > 0)
            {
                lines.Add($"{Label("Bounds", "Bounds")}: {Bounds(min, max)}");
            }

            return new Result(lines, ShowSource: false);
        }

        string text = Encoding.UTF8.GetString(content);
        string[] rows = text.Split('\n');
        string header = rows.Select(row => row.Trim()).FirstOrDefault(row => row.Length > 0) ?? string.Empty;
        if (!header.StartsWith("solid", StringComparison.Ordinal))
        {
            throw new FormatException("Not an STL model.");
        }

        int facets = 0;
        foreach (string raw in rows)
        {
            string row = raw.Trim();
            if (row.StartsWith("facet", StringComparison.Ordinal))
            {
                facets++;
            }
            else if (row.StartsWith("vertex", StringComparison.Ordinal))
            {
                ExtendBounds(row["vertex".Length..].Trim(), min, max);
            }
        }

        lines.Add($"{Label("Format", "Format")}: {Label("Ascii", "ASCII")}");
        string name = header["solid".Length..].Trim();
        if (name.Length > 0)
        {
            lines.Add($"{Label("Name", "Name")}: {name}");
        }

        lines.Add($"{Label("Triangles", "Triangles")}: {facets}");
        if (facets > 0)
        {
            lines.Add($"{Label("Bounds", "Bounds")}: {Bounds(min, max)}");
        }

        return new Result(lines, ShowSource: true);
    }

    /// <summary>
    /// A binary STL is an 80-byte header, a count and that many 50-byte triangles. The layout is trusted when the length
    /// matches it exactly: many binary files begin with "solid" as well, so the word alone does not decide.
    /// </summary>
    private static bool IsBinaryStl(byte[] content, out uint declared)
    {
        declared = 0;
        if (content.Length < StlHeaderLength)
        {
            return false;
        }

        declared = BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(80, 4));
        if (StlHeaderLength + (long)declared * StlTriangleLength == content.Length)
        {
            return true;
        }

        return !Encoding.ASCII.GetString(content.AsSpan(0, Math.Min(content.Length, 256))).TrimStart().StartsWith("solid", StringComparison.Ordinal);
    }

    #endregion

    private static string ListSuffix(IReadOnlyList<string> names) =>
        names.Count == 0 ? string.Empty : $" ({string.Join(", ", names.Take(20))}{(names.Count > 20 ? ", …" : string.Empty)})";

    private static double ParseDouble(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
}

using System.Globalization;
using System.Text.Json.Nodes;
using iConsole.Json;
using iConsole.Structured;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace iConsole.Yaml;

/// <summary>
/// Reads YAML text into a JSON-shaped tree. Plain scalars take the YAML 1.2 core types (<c>null</c>, <c>~</c>, booleans,
/// integers, floats); quoted scalars are always text. A file with several documents reads as an array of them. An alias
/// that points back at its own parent is refused, and so is a file that nests deeper than the JSON viewer reads or holds
/// more than <see cref="StructuredTree.MaxNodes"/> values.
/// </summary>
public static class YamlDocumentReader
{
    /// <summary>The tree of the text. Throws <see cref="YamlException"/> for a syntax error and <see cref="FormatException"/> for a file the viewer will not read.</summary>
    public static JsonNode? Read(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        if (stream.Documents.Count == 0)
        {
            return null;
        }

        if (stream.Documents.Count == 1)
        {
            return new Converter().Convert(stream.Documents[0].RootNode, 0);
        }

        var converter = new Converter();
        var documents = new JsonArray();
        foreach (YamlDocument document in stream.Documents)
        {
            documents.Add(converter.Convert(document.RootNode, 0));
        }

        return documents;
    }

    private sealed class Converter
    {
        private readonly HashSet<YamlNode> _path = new(ReferenceEqualityComparer.Instance);
        private int _count;

        public JsonNode? Convert(YamlNode node, int depth)
        {
            if (++_count > StructuredTree.MaxNodes)
            {
                throw new FormatException("the file holds too many values");
            }

            if (depth > JsonTree.MaxDepth)
            {
                throw new FormatException("the file nests too deeply");
            }

            switch (node)
            {
                case YamlScalarNode scalar:
                    return Scalar(scalar);

                case YamlMappingNode mapping:
                    return Enter(mapping, () => Mapping(mapping, depth));

                case YamlSequenceNode sequence:
                    return Enter(sequence, () => Sequence(sequence, depth));

                default:
                    return null;
            }
        }

        // An alias is the same node object as its anchor, so a node already on the path is an alias that points back up.
        private JsonNode? Enter(YamlNode node, Func<JsonNode> build)
        {
            if (!_path.Add(node))
            {
                throw new FormatException("an alias refers to its own parent");
            }

            try
            {
                return build();
            }
            finally
            {
                _path.Remove(node);
            }
        }

        private JsonObject Mapping(YamlMappingNode mapping, int depth)
        {
            var result = new JsonObject();
            foreach (KeyValuePair<YamlNode, YamlNode> pair in mapping.Children)
            {
                if (pair.Key is not YamlScalarNode { Value: { } key })
                {
                    throw new FormatException("a key that is not text is not supported");
                }

                // The last of two equal keys wins, as it does in most YAML readers.
                result[key] = Convert(pair.Value, depth + 1);
            }

            return result;
        }

        private JsonArray Sequence(YamlSequenceNode sequence, int depth)
        {
            var result = new JsonArray();
            foreach (YamlNode item in sequence.Children)
            {
                result.Add(Convert(item, depth + 1));
            }

            return result;
        }

        private static JsonNode? Scalar(YamlScalarNode scalar)
        {
            string text = scalar.Value ?? string.Empty;
            if (scalar.Style != ScalarStyle.Plain)
            {
                return JsonValue.Create(text);
            }

            switch (text)
            {
                case "" or "~" or "null" or "Null" or "NULL":
                    return null;
                case "true" or "True" or "TRUE":
                    return JsonValue.Create(true);
                case "false" or "False" or "FALSE":
                    return JsonValue.Create(false);
            }

            if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer))
            {
                return JsonValue.Create(integer);
            }

            if (text.StartsWith("0x", StringComparison.Ordinal)
                && long.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long hex))
            {
                return JsonValue.Create(hex);
            }

            if (IsFloat(text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                && double.IsFinite(number))
            {
                return JsonValue.Create(number);
            }

            return JsonValue.Create(text);
        }

        // The YAML 1.2 core float: digits with an optional point and exponent. "inf", "nan" and friends stay text.
        private static bool IsFloat(string text)
        {
            int i = 0;
            if (i < text.Length && (text[i] == '+' || text[i] == '-'))
            {
                i++;
            }

            int digits = 0;
            while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; digits++; }
            if (i < text.Length && text[i] == '.')
            {
                i++;
                while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; digits++; }
            }

            if (digits == 0)
            {
                return false;
            }

            if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
            {
                i++;
                if (i < text.Length && (text[i] == '+' || text[i] == '-'))
                {
                    i++;
                }

                int exponent = 0;
                while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; exponent++; }
                if (exponent == 0)
                {
                    return false;
                }
            }

            return i == text.Length;
        }
    }
}

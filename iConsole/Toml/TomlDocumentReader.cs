using System.Text.Json.Nodes;
using iConsole.Json;
using iConsole.Structured;
using Tomlyn;
using Tomlyn.Model;

namespace iConsole.Toml;

/// <summary>
/// Reads TOML text into a JSON-shaped tree. Tables and arrays of tables become objects and arrays; dates and times stay as
/// their TOML text, since JSON has no such type. Throws <see cref="TomlException"/> for a syntax error, and
/// <see cref="FormatException"/> for a file that nests deeper than the JSON viewer reads or holds too many values.
/// </summary>
public static class TomlDocumentReader
{
    public static JsonNode? Read(string text)
    {
        TomlTable table = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        int count = 0;
        return Convert(table, 0, ref count);
    }

    private static JsonNode? Convert(object? value, int depth, ref int count)
    {
        if (++count > StructuredTree.MaxNodes)
        {
            throw new FormatException("the file holds too many values");
        }

        if (depth > JsonTree.MaxDepth)
        {
            throw new FormatException("the file nests too deeply");
        }

        switch (value)
        {
            case null:
                return null;

            case TomlTable table:
                var members = new JsonObject();
                foreach (KeyValuePair<string, object> pair in table)
                {
                    members[pair.Key] = Convert(pair.Value, depth + 1, ref count);
                }

                return members;

            // Tomlyn's arrays are generic lists (an array of tables included), so any other sequence is an array.
            case System.Collections.IEnumerable sequence when value is not string:
                var items = new JsonArray();
                foreach (object? item in sequence)
                {
                    items.Add(Convert(item, depth + 1, ref count));
                }

                return items;

            case string text:
                return JsonValue.Create(text);

            case bool flag:
                return JsonValue.Create(flag);

            case long integer:
                return JsonValue.Create(integer);

            case double number:
                return double.IsFinite(number) ? JsonValue.Create(number) : JsonValue.Create(number.ToString(System.Globalization.CultureInfo.InvariantCulture));

            default:
                // TomlDateTime and anything else TOML adds: its TOML text, which is what the file says.
                return JsonValue.Create(value.ToString() ?? string.Empty);
        }
    }
}

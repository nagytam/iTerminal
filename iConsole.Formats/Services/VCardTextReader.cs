using iConsole.Helper;

namespace iConsole.Services;

/// <summary>
/// Reads a vCard (.vcf) address book into lines: one block per contact, headed by its name, then one line for each
/// property. Structured values are spelled out (a name as "Given Family", an address on one line), a TYPE parameter
/// follows the value in brackets, and a photo is shown as a note rather than its base64 text.
/// </summary>
public static class VCardTextReader
{
    /// <summary>Property names shown with a label; any other property is shown under its own name.</summary>
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ORG"] = "Organization", ["TITLE"] = "Title", ["ROLE"] = "Role", ["EMAIL"] = "Email", ["TEL"] = "Phone",
        ["ADR"] = "Address", ["URL"] = "Web", ["BDAY"] = "Birthday", ["ANNIVERSARY"] = "Anniversary", ["NICKNAME"] = "Nickname",
        ["NOTE"] = "Note", ["CATEGORIES"] = "Categories", ["IMPP"] = "Messaging", ["GENDER"] = "Gender",
    };

    /// <summary>Properties that describe the file rather than the contact, and so are left out.</summary>
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "BEGIN", "END", "VERSION", "PRODID", "REV", "UID", "PHOTO", "LOGO", "SOUND", "KEY", "N", "FN",
    };

    public static IReadOnlyList<string> Read(byte[] content)
    {
        List<string> lines = [];
        try
        {
            string text = TextEncodings.Decode(content, out _);
            List<ContentLine> card = [];
            bool inCard = false;
            int number = 0;
            foreach (string logical in ContentLines.Unfold(text))
            {
                ContentLine? parsed = ContentLines.Parse(logical);
                if (parsed is not { } property)
                {
                    continue;
                }

                if (property.Name.Equals("BEGIN", StringComparison.OrdinalIgnoreCase)
                    && property.Value.Equals("VCARD", StringComparison.OrdinalIgnoreCase))
                {
                    card = [];
                    inCard = true;
                }
                else if (property.Name.Equals("END", StringComparison.OrdinalIgnoreCase)
                    && property.Value.Equals("VCARD", StringComparison.OrdinalIgnoreCase))
                {
                    if (inCard)
                    {
                        number++;
                        if (number > 1)
                        {
                            lines.Add(string.Empty);
                        }

                        AppendCard(lines, card, number);
                    }

                    inCard = false;
                }
                else if (inCard)
                {
                    card.Add(WithoutGroup(property));
                }
            }

            if (number == 0)
            {
                lines.Add("[No contacts were found in this file]");
            }
        }
        catch (Exception exception)
        {
            lines.Add($"[Could not read the contacts: {exception.Message}]");
        }

        return lines;
    }

    private static void AppendCard(List<string> lines, List<ContentLine> card, int number)
    {
        string? name = Field(card, "FN");
        if (string.IsNullOrWhiteSpace(name))
        {
            name = StructuredName(card) ?? $"Contact {number}";
        }

        lines.Add(name);
        lines.Add(new string('-', Math.Max(3, name.Length)));
        foreach (ContentLine property in card)
        {
            if (Skipped.Contains(property.Name))
            {
                if (property.Name.Equals("PHOTO", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("LOGO", StringComparison.OrdinalIgnoreCase))
                {
                    lines.Add($"{Label(property.Name)}: [embedded image]");
                }

                continue;
            }

            string value = Describe(property);
            if (value.Length > 0)
            {
                ContentLines.AppendValue(lines, $"{Label(property.Name)}: ", "    ", value);
            }
        }
    }

    /// <summary>A property grouped under a name, as item1.URL is, is shown under the bare name; the group only ties it to its label.</summary>
    private static ContentLine WithoutGroup(ContentLine property)
    {
        int dot = property.Name.LastIndexOf('.');
        return dot < 0 ? property : property with { Name = property.Name[(dot + 1)..] };
    }

    private static string Describe(ContentLine property)
    {
        string value = property.Name.ToUpperInvariant() switch
        {
            // Address and organization parts are separated by ';', and an empty part is left out.
            "ADR" or "ORG" => string.Join(", ", ContentLines.SplitStructured(property.Value, ';').Where(part => part.Length > 0)),
            // A category list is separated by ','.
            "CATEGORIES" => string.Join(", ", ContentLines.SplitStructured(property.Value, ',').Where(part => part.Length > 0)),
            _ => ContentLines.Unescape(property.Value),
        };

        string types = string.Join(", ", property.ParameterValues("TYPE").Select(t => t.ToLowerInvariant()));
        if (types.Length > 0 && value.Length > 0)
        {
            value += $" ({types})";
        }

        return value.Trim();
    }

    private static string? Field(List<ContentLine> card, string name)
    {
        foreach (ContentLine property in card)
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return ContentLines.Unescape(property.Value);
            }
        }

        return null;
    }

    /// <summary>The name built from N (family;given;additional;prefix;suffix), used when the card has no formatted name.</summary>
    private static string? StructuredName(List<ContentLine> card)
    {
        string? n = Field(card, "N");
        if (n is null)
        {
            return null;
        }

        List<string> parts = ContentLines.SplitStructured(n, ';');
        string given = parts.Count > 1 ? parts[1] : string.Empty;
        string family = parts.Count > 0 ? parts[0] : string.Empty;
        string text = string.Join(' ', new[] { given, family }.Where(part => part.Length > 0));
        return text.Length > 0 ? text : null;
    }

    private static string Label(string name) =>
        Labels.TryGetValue(name, out string? label) ? label
        : name.Equals("PHOTO", StringComparison.OrdinalIgnoreCase) ? "Photo"
        : name.Equals("LOGO", StringComparison.OrdinalIgnoreCase) ? "Logo"
        : name.Equals("FN", StringComparison.OrdinalIgnoreCase) ? "Name"
        : name.Equals("N", StringComparison.OrdinalIgnoreCase) ? "Name"
        : name;
}

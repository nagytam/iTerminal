namespace iTerminal.Core.Services.Labels;

/// <summary>The colours a file or folder can be labelled with. None is the absence of a colour.</summary>
public enum LabelColour
{
    None,
    Red,
    Orange,
    Yellow,
    Green,
    Blue,
    Purple,
    Grey,
}

/// <summary>What a file or folder is labelled with: one colour, and any number of tags.</summary>
public sealed record FileLabel(LabelColour Colour, IReadOnlyList<string> Tags)
{
    public static FileLabel None { get; } = new(LabelColour.None, []);

    public bool IsEmpty => Colour == LabelColour.None && Tags.Count == 0;
}

/// <summary>The tags as typed: separated by commas, trimmed, with blanks and repeats (ignoring case) dropped.</summary>
public static class FileLabelTags
{
    public const int MaxTags = 20;
    public const int MaxTagLength = 40;

    public static IReadOnlyList<string> Parse(string? text) =>
        (text ?? string.Empty)
            .Split(',')
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string Format(IEnumerable<string> tags) => string.Join(", ", tags);

    /// <summary>What is wrong with the tags typed, or null when they can be saved.</summary>
    public static string? Validate(string text)
    {
        var tags = Parse(text);
        if (tags.Count > MaxTags)
        {
            return $"At most {MaxTags} tags.";
        }

        return tags.Any(tag => tag.Length > MaxTagLength) ? $"A tag is at most {MaxTagLength} characters." : null;
    }
}

public static class LabelColours
{
    public static string Name(LabelColour colour) => colour.ToString().ToLowerInvariant();

    /// <summary>The colour a stored name means; anything unknown is no colour.</summary>
    public static LabelColour Parse(string? name) =>
        Enum.GetValues<LabelColour>()
            .FirstOrDefault(colour => string.Equals(colour.ToString(), name, StringComparison.OrdinalIgnoreCase));
}

using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Commands;

namespace iTerminal.Core.Services.Labels;

/// <summary>
/// Which labelled entries a panel shows: a colour and/or a tag. A null colour is any colour, and <see cref="LabelColour.None"/>
/// is only the entries without one. A null tag is any tags; otherwise the entry must have that tag, ignoring case.
/// </summary>
public sealed record LabelFilter(LabelColour? Colour, string? Tag)
{
    /// <summary>The filter that shows everything: no colour and no tag.</summary>
    public bool IsEmpty => Colour is null && Tag is null;

    public bool Matches(FileLabel label) =>
        (Colour is not { } colour || label.Colour == colour)
        && (Tag is null || label.Tags.Any(tag => string.Equals(tag, Tag, StringComparison.OrdinalIgnoreCase)));

    /// <summary>What the footer says: "label: red, tag invoice".</summary>
    public string Describe()
    {
        List<string> parts = [];
        if (Colour is { } colour)
        {
            parts.Add(colour == LabelColour.None ? "no colour" : LabelColours.Name(colour));
        }

        if (Tag is not null)
        {
            parts.Add("tag " + Tag);
        }

        return "label: " + string.Join(", ", parts);
    }

    /// <summary>
    /// The predicate a panel filters with. The parent row and folders are always shown, so a filtered panel can still be
    /// walked, and so is anything that is not a local file, since it has no label to match.
    /// </summary>
    public Func<IEntry, bool> ToEntryFilter(IFileLabelService labels) => entry =>
        FocusedEntry.IsParentEntry(entry)
        || entry.Url.ContainerType != ContainerType.FileSystem
        || entry.Url.PathType == PathType.Folder
        || Matches(labels.Get(entry.Url.Path));
}

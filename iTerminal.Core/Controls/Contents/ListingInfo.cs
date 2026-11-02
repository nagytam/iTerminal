using iConsole.Interfaces;

namespace iTerminal.Core.Controls.Contents;

/// <summary>One label and value line of the Info listing.</summary>
public sealed record ListingInfoRow(string Label, string Value);

/// <summary>
/// The rows of the Info listing for one entry, as a pure function of the entry and the localized labels. Only
/// fields the entry already carries are used: nothing here touches the disk.
/// </summary>
public static class ListingInfo
{
    /// <summary>
    /// The rows for <paramref name="entry"/>, or the single "no entry" row when there is none, when it is the
    /// <c>..</c> row, or when its Url is missing.
    /// </summary>
    /// <param name="entry">The other panel's cursor entry, or null.</param>
    /// <param name="text">Localized text by key; the key doubles as the fallback in tests.</param>
    public static IReadOnlyList<ListingInfoRow> Describe(IEntry? entry, Func<string, string> text)
    {
        if (entry?.Url is not { } url || url.DisplayName == "..")
        {
            return [new ListingInfoRow(text("Listing.Entry"), text("Listing.NoEntry"))];
        }

        var type = url.IsLink
            ? text("Listing.TypeLink")
            : url.PathType == iConsole.Data.PathType.Folder ? text("Listing.TypeFolder") : text("Listing.TypeFile");
        var attributes = EntryBaseAttributes(url);

        var rows = new List<ListingInfoRow>
        {
            new(text("Listing.Name"), url.Name),
            new(text("Listing.Path"), url.FullName),
            new(text("Listing.Type"), type),
            new(text("Listing.Size"), url.FileSize),
            new(text("Listing.Modified"), $"{url.Date} {url.Time}".Trim()),
            new(text("Listing.Attributes"), attributes.Length == 0 ? text("Listing.Unknown") : attributes),
        };
        if (url.IsLink)
        {
            rows.Add(new(text("Listing.LinkTarget"), url.LinkTarget ?? text("Listing.Unknown")));
        }

        return rows;
    }

    /// <summary>The Long listing attributes text, or empty when the Url carries none.</summary>
    private static string EntryBaseAttributes(iConsole.Data.Url url) =>
        url.UnixMode is null && url.Attributes is null
            ? string.Empty
            : iTerminal.Core.Model.ContentModels.EntryBase.FormatAttributes(url).TrimEnd();
}

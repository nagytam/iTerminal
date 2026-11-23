using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// Answers "is this row in the panel's listing" from the model's current entries, with no I/O. A
/// <c>CanExecute</c> is read on every draw of its footer button, so the gate that a button reads must not
/// ask a remote server. The copy and move tasks keep asking the server, because they run on a background
/// task and a file made since the listing must still stop an overwrite.
/// </summary>
internal static class ContentModelListing
{
    /// <summary>The listed row that <paramref name="url"/> names, or null. The <c>..</c> row never counts.</summary>
    public static Url? FindListed(IContentModel model, Url url)
    {
        foreach (var entry in model.Entries.AsSnapshot())
        {
            if (entry.DisplayName != ".." && ContentModelBase.IsSameLocation(entry.Url.FullUrl, url.FullUrl))
            {
                return entry.Url;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="url"/> exists for a footer gate: a remote drive answers from its listing, every
    /// other drive asks <see cref="IContentModel.ExistsFile"/> or <see cref="IContentModel.ExistsFolder"/>.
    /// </summary>
    public static bool IsShown(IContentModel model, Url url) =>
        model is ContentModelRemoteDrive
            ? FindListed(model, url) is not null
            : model.ExistsFile(url) || model.ExistsFolder(url);

    /// <summary>
    /// Whether the F3 and F4 gates may treat <paramref name="url"/> as an existing file. A remote drive answers
    /// from its listing; every other drive answers through <see cref="IContentModel.ExistsFile"/>, which is cheap there.
    /// </summary>
    public static bool FileIsShown(IContentModel model, Url url) =>
        model is ContentModelRemoteDrive
            ? FindListed(model, url) is { PathType: PathType.File }
            : model.ExistsFile(url);
}

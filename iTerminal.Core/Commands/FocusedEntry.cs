using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Commands;

/// <summary>
/// What the file operations (F5 copy, Shift+F6 move, F6 rename, F8 delete) may act on: the file or
/// folder under the cursor — never the ".." row.
///
/// <para>The ".." row's URL is the parent folder, and that folder exists, so a check that only
/// asked "does this URL exist" let every one of them through on it. F8 on ".." offered to delete
/// the folder the user was standing in, with "Recursively delete folder" already ticked — one Enter
/// from removing everything in it.</para>
/// </summary>
public static class FocusedEntry
{
    public const string ParentEntryName = "..";

    /// <summary>Whether <paramref name="entry"/> is the row that leads to the parent folder.</summary>
    public static bool IsParentEntry(IEntry? entry) =>
        entry is not null && string.Equals(entry.DisplayName?.Trim(), ParentEntryName, StringComparison.Ordinal);

    /// <summary>
    /// The URL of the entry under <paramref name="model"/>'s cursor when it is a file or folder that
    /// exists, or null when there is nothing a file operation may touch — which includes a row that
    /// stands for something other than a file (a container, <see cref="Model.ContentModels.IDriveObjectEntry"/>).
    /// </summary>
    public static Url? ActionableUrl(IContentModel? model)
    {
        var entry = model?.GetFocusedEntry();
        if (model is null || entry is null || IsParentEntry(entry) || entry is Model.ContentModels.IDriveObjectEntry)
        {
            return null;
        }

        var url = entry.Url;
        return url is not null && (model.ExistsFile(url) || model.ExistsFolder(url)) ? url : null;
    }

    /// <summary>
    /// The last segment of <paramref name="url"/> — the file or folder's own name. <see cref="Url.Name"/>
    /// is empty for a folder URL (it ends in a separator), and a copy that named its target by it
    /// would land a marked folder on top of the target folder itself.
    /// </summary>
    public static string NameOf(Url url)
    {
        if (!string.IsNullOrEmpty(url.Name))
        {
            return url.Name;
        }

        var path = url.FullUrl.TrimEnd('/', '\\');
        var cut = path.LastIndexOfAny(['/', '\\']);
        return cut >= 0 ? path[(cut + 1)..] : path;
    }

    /// <summary>
    /// What F5, Shift+F6 and F8 act on in <paramref name="model"/>: the marked rows when there are
    /// any — wherever the cursor is, as in every commander — otherwise the row under the cursor
    /// (<see cref="ActionableUrl"/>). Empty when there is nothing to act on.
    /// </summary>
    public static IReadOnlyList<Url> ActionableUrls(IContentModel? model)
    {
        if (model is Model.ContentModels.IMarkableContentModel marks)
        {
            var marked = marks.GetMarkedEntries();
            if (marked.Count > 0)
            {
                return [.. marked.Where(e => !IsParentEntry(e) && e is not Model.ContentModels.IDriveObjectEntry).Select(e => e.Url)];
            }
        }

        return ActionableUrl(model) is { } url ? [url] : [];
    }
}

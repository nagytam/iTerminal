using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iTerminal.Core.Commands;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Helper;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Archives;
using Microsoft.Extensions.AI;
using System.Text;

namespace iTerminal.Core.AI;

/// <summary>
/// The AI tools for the two file panels: reading what a panel shows, moving the cursor, marking, sorting,
/// filtering and the file operations on the marked rows or the named ones.
///
/// <para>Every tool takes a <c>panel</c> argument — <c>left</c>, <c>right</c>, <c>focused</c> (the default) or
/// <c>other</c> — and works on the panel's content model, the same layer the keyboard commands use, so a
/// tool and a key press see the same marks, sort and filter. A tool never throws at the model: a failure
/// comes back as text the model can read and act on.</para>
///
/// <para>Copy, move and delete run on the calling (background) thread and never inside a key handler. Delete
/// moves to the recycle bin and needs <c>confirmed=true</c> to show an approval dialog. Only an explicit yes
/// in that dialog authorizes it; the model cannot approve its own action.</para>
/// </summary>
public partial class AiPanelTools(
    IContentModelService contentModelService,
    IArchiveCompressionService? compressionService = null,
    IArchiveExtractionService? extractionService = null,
    FileSystemResolver? fileSystemResolver = null,
    IAiActionApprovalService? approvalService = null)
{
    /// <summary>The most rows a listing tool returns, so a huge folder cannot swamp the model's context.</summary>
    internal const int MaxListedEntries = 200;

    public IReadOnlyList<AITool> Create() =>
    [
        AIFunctionFactory.Create(GetPanelState, "get_panel_state",
            "Describe a file panel: its path, the number of entries, the entry under the cursor, the marked count, the sort and the filter. panel: left, right, focused (default) or other."),
        AIFunctionFactory.Create(ListPanelEntries, "list_panel_entries",
            "List the entries a panel shows now (after its filter and sort). Folders end in '/', '>' marks the cursor row, '*' a marked row. panel: left, right, focused or other. max defaults to 100."),
        AIFunctionFactory.Create(GetFocusedEntry, "get_focused_entry",
            "Get the name, size and date of the entry under a panel's cursor."),
        AIFunctionFactory.Create(GetMarkedEntries, "get_marked_entries",
            "List the names of the entries marked in a panel."),
        AIFunctionFactory.Create(MoveCursorToEntry, "move_cursor_to_entry",
            "Put a panel's cursor on the entry with this name (case-insensitive; a unique prefix also works)."),
        AIFunctionFactory.Create(MoveCursor, "move_cursor",
            "Move a panel's cursor: where is one of first, last, up, down, page_up, page_down."),
        AIFunctionFactory.Create(OpenFocusedEntry, "open_focused_entry",
            "Enter the folder under a panel's cursor (or its parent for the '..' row). Files are not opened."),
        AIFunctionFactory.Create(GoToParentFolder, "go_to_parent_folder",
            "Navigate a panel to its parent folder."),
        AIFunctionFactory.Create(MarkEntries, "mark_entries",
            "Mark entries by pattern, e.g. '*.cs;*.md' (names, ; separated). Set marked=false to unmark instead. includeFolders defaults to false."),
        AIFunctionFactory.Create(MarkEntryByName, "mark_entry",
            "Mark or unmark one entry by its exact name."),
        AIFunctionFactory.Create(InvertMarks, "invert_marks",
            "Flip the mark of every listed file in a panel."),
        AIFunctionFactory.Create(ClearMarks, "clear_marks",
            "Remove every mark in a panel."),
        AIFunctionFactory.Create(SetPanelSort, "set_panel_sort",
            "Sort a panel. mode: as_listed, name, extension, size or date. descending reverses the order."),
        AIFunctionFactory.Create(SetPanelFilter, "set_panel_filter",
            "Show only entries whose name contains this text. An empty string clears the filter."),
        AIFunctionFactory.Create(RefreshPanel, "refresh_panel",
            "Re-read a panel's folder from disk."),
        AIFunctionFactory.Create(SwapPanelPaths, "swap_panels",
            "Show the left panel's folder in the right panel and the right panel's folder in the left."),
        AIFunctionFactory.Create(SyncOtherPanel, "open_same_folder_in_other_panel",
            "Navigate the other panel to the folder a panel shows."),
        AIFunctionFactory.Create(CreateFolder, "create_folder",
            "Create a sub-folder in the folder a panel shows."),
        AIFunctionFactory.Create(CopyToOtherPanel, "copy_to_other_panel",
            "Copy the marked entries of a panel (or, when none is marked, the entry under its cursor; or the names given, ';'-separated) into the folder of the other panel. Never overwrites."),
        AIFunctionFactory.Create(MoveToOtherPanel, "move_to_other_panel",
            "Move the marked entries of a panel (or the one under the cursor; or the names given, ';'-separated) into the folder of the other panel. Never overwrites."),
        AIFunctionFactory.Create(RenameEntry, "rename_entry",
            "Rename an entry of a panel (the one under the cursor when name is empty) to a new name within the same folder."),
        AIFunctionFactory.Create(DeleteEntries, "delete_entries",
            "Move the marked entries of a panel (or the one under the cursor; or the names given, ';'-separated) to the recycle bin. DESTRUCTIVE: first ask the user in chat, then pass confirmed=true to show an app approval dialog listing every target. The user must approve there; this parameter is not authorization."),
        .. CreateArchiveAndFolderTools(),
    ];

    #region Panel resolution

    internal IContentModel? ResolvePanel(string? panel)
    {
        string which = (panel ?? "focused").Trim().ToLowerInvariant();
        IContentModel? focused = contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();
        switch (which)
        {
            case "left":
                return Find(Content.Left);
            case "right":
                return Find(Content.Right);
            case "other":
                {
                    if (focused is null)
                    {
                        return null;
                    }
                    string otherName = focused.Name == Content.Left ? Content.Right : Content.Left;
                    return Find(otherName);
                }
            default:
                return focused;
        }
    }

    private IContentModel? Find(string name) => contentModelService.ContentModels.FirstOrDefault(m => m.Name == name);

    private bool TryPanel(string? panel, out IContentModel model, out string error)
    {
        IContentModel? found = ResolvePanel(panel);
        if (found is null)
        {
            model = null!;
            error = $"Error: there is no '{panel ?? "focused"}' panel. Use left, right, focused or other.";
            return false;
        }

        model = found;
        error = string.Empty;
        return true;
    }

    private static string PanelLabel(IContentModel model) => model.Name == Content.Left ? "left" : model.Name == Content.Right ? "right" : model.Name;

    #endregion

    #region Reading

    internal string GetPanelState(string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Panel: {PanelLabel(model)}");
        sb.AppendLine($"Path: {model.Url?.FullUrl}");
        sb.AppendLine($"Entries shown: {model.FilteredEntries.Count} (of {model.Entries.Count})");
        sb.AppendLine($"Cursor: {DescribeEntry(model.GetFocusedEntry())}");
        if (model is IMarkableContentModel marks)
        {
            sb.AppendLine($"Marked: {marks.MarkedCount}");
        }
        if (model is ISortableContentModel sortable)
        {
            sb.AppendLine($"Sort: {(sortable.Sort.IsAsListed ? "as listed" : sortable.Sort.Describe())}");
        }
        sb.AppendLine($"Filter: {(string.IsNullOrEmpty(model.Filter) ? "(none)" : model.Filter)}");
        sb.Append($"Read-only: {(model.IsReadOnly ? "yes" : "no")}");
        return sb.ToString();
    }

    internal string ListPanelEntries(string? panel = null, int max = 100)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }

        int limit = Math.Clamp(max, 1, MaxListedEntries);
        IList<IEntry> entries = model.FilteredEntries;
        IEntry? cursor = model.GetFocusedEntry();
        var marks = model as IMarkableContentModel;
        var sb = new StringBuilder();
        int shown = 0;
        foreach (IEntry entry in entries)
        {
            if (shown++ >= limit)
            {
                break;
            }
            string cursorMark = ReferenceEquals(entry, cursor) ? ">" : " ";
            string markMark = marks?.IsMarked(entry) == true ? "*" : " ";
            sb.AppendLine($"{cursorMark}{markMark} {DescribeEntry(entry)}");
        }

        if (entries.Count > limit)
        {
            sb.AppendLine($"... {entries.Count - limit} more not shown (raise max, up to {MaxListedEntries}, or set_panel_filter).");
        }
        if (entries.Count == 0)
        {
            sb.Append("(empty)");
        }
        return sb.ToString().TrimEnd();
    }

    internal string GetFocusedEntry(string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        IEntry? entry = model.GetFocusedEntry();
        return entry is null ? "Nothing is under the cursor." : $"{DescribeEntry(entry)}\nUrl: {entry.Url?.FullUrl}";
    }

    internal string GetMarkedEntries(string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        if (model is not IMarkableContentModel marks || !marks.CanMark)
        {
            return "This panel does not support marking.";
        }
        IReadOnlyList<IEntry> marked = marks.GetMarkedEntries();
        return marked.Count == 0 ? "No entries are marked." : string.Join("\n", marked.Select(DescribeEntry));
    }

    private static string DescribeEntry(IEntry? entry)
    {
        if (entry is null)
        {
            return "(none)";
        }

        bool isFolder = entry.Url?.PathType == PathType.Folder;
        string name = entry.DisplayName?.Trim() ?? entry.Name;
        string details = string.Join(' ', new[] { entry.FileSize?.Trim(), entry.Date?.Trim(), entry.Time?.Trim() }.Where(s => !string.IsNullOrEmpty(s)));
        return $"{name}{(isFolder && !name.EndsWith('/') ? "/" : string.Empty)}{(details.Length > 0 ? "  " + details : string.Empty)}";
    }

    #endregion

    #region Cursor and navigation

    internal string MoveCursorToEntry(string name, string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }

        IEntry? entry = FindEntry(model, name, out string problem);
        if (entry is null)
        {
            return problem;
        }

        int index = model.FilteredEntries.IndexOf(entry);
        model.CursorPosition.Y = index;
        return $"Cursor is on {DescribeEntry(entry)}.";
    }

    internal string MoveCursor(string where, string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }

        if (!IsKnownDirection(where))
        {
            return "Error: where must be one of first, last, up, down, page_up, page_down.";
        }

        int last = Math.Max(0, model.FilteredEntries.Count - 1);
        long current = model.CursorPosition.Y;
        long target = where!.Trim().ToLowerInvariant() switch
        {
            "first" or "top" or "home" => 0,
            "last" or "bottom" or "end" => last,
            "up" or "previous" => current - 1,
            "down" or "next" => current + 1,
            "page_up" => current - 10,
            _ => current + 10,
        };

        model.CursorPosition.Y = Math.Clamp(target, 0, last);
        return $"Cursor is on {DescribeEntry(model.GetFocusedEntry())}.";
    }

    private static bool IsKnownDirection(string? where) =>
        (where ?? string.Empty).Trim().ToLowerInvariant() is "first" or "top" or "home" or "last" or "bottom" or "end"
            or "up" or "previous" or "down" or "next" or "page_up" or "page_down";

    internal async Task<string> OpenFocusedEntry(string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }

        IEntry? entry = model.GetFocusedEntry();
        if (entry?.Url is null)
        {
            return "Nothing is under the cursor.";
        }
        if (entry.Url.PathType != PathType.Folder)
        {
            return $"{entry.DisplayName?.Trim()} is a file; only folders can be entered.";
        }

        await contentModelService.SetUrlAsync(model.Name, entry.Url);
        return $"{PanelLabel(model)} panel is now at {entry.Url.FullUrl}";
    }

    internal async Task<string> GoToParentFolder(string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }

        IEntry? parent = model.Entries.FirstOrDefault(FocusedEntry.IsParentEntry);
        if (parent?.Url is null)
        {
            return "This folder has no parent.";
        }

        await contentModelService.SetUrlAsync(model.Name, parent.Url);
        return $"{PanelLabel(model)} panel is now at {parent.Url.FullUrl}";
    }

    internal async Task<string> SwapPanelPaths()
    {
        IContentModel? left = Find(Content.Left);
        IContentModel? right = Find(Content.Right);
        if (left?.Url is null || right?.Url is null)
        {
            return "Error: both panels must be showing a folder.";
        }

        Url leftUrl = left.Url;
        Url rightUrl = right.Url;
        await contentModelService.SetUrlAsync(Content.Left, rightUrl);
        await contentModelService.SetUrlAsync(Content.Right, leftUrl);
        return $"Swapped: left is {rightUrl.FullUrl}, right is {leftUrl.FullUrl}";
    }

    internal async Task<string> SyncOtherPanel(string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        IContentModel? other = model.Name == Content.Left ? Find(Content.Right) : Find(Content.Left);
        if (other is null || model.Url is null)
        {
            return "Error: there is no other panel.";
        }

        await contentModelService.SetUrlAsync(other.Name, model.Url);
        return $"{PanelLabel(other)} panel is now at {model.Url.FullUrl}";
    }

    #endregion

    #region Marks, sort, filter

    internal string MarkEntries(string patterns, string? panel = null, bool marked = true, bool includeFolders = false)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        if (model is not IMarkableContentModel marks || !marks.CanMark)
        {
            return "This panel does not support marking.";
        }
        if (string.IsNullOrWhiteSpace(patterns))
        {
            return "Error: give at least one pattern, e.g. '*.txt'.";
        }

        int changed = marks.SetMarks(patterns, marked, includeFolders);
        return $"{(marked ? "Marked" : "Unmarked")} {changed} entries matching '{patterns}'. {marks.MarkedCount} marked in total.";
    }

    internal string MarkEntryByName(string name, bool marked = true, string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        if (model is not IMarkableContentModel marks || !marks.CanMark)
        {
            return "This panel does not support marking.";
        }

        IEntry? entry = FindEntry(model, name, out string problem);
        if (entry is null)
        {
            return problem;
        }

        return marks.SetMark(entry, marked)
            ? $"{(marked ? "Marked" : "Unmarked")} {entry.DisplayName?.Trim()}. {marks.MarkedCount} marked in total."
            : $"{entry.DisplayName?.Trim()} was left as it was.";
    }

    internal string InvertMarks(string? panel = null, bool includeFolders = false)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        if (model is not IMarkableContentModel marks || !marks.CanMark)
        {
            return "This panel does not support marking.";
        }

        marks.InvertMarks(includeFolders);
        return $"{marks.MarkedCount} marked.";
    }

    internal string ClearMarks(string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        if (model is not IMarkableContentModel marks)
        {
            return "This panel does not support marking.";
        }

        marks.ClearMarks();
        return "All marks cleared.";
    }

    internal string SetPanelSort(string mode, string? panel = null, bool descending = false)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        if (model is not ISortableContentModel sortable || !sortable.CanSort)
        {
            return "This panel's order is its content and cannot be sorted.";
        }

        string text = (mode ?? string.Empty).Trim().Replace("_", string.Empty).Replace(" ", string.Empty);
        if (!Enum.TryParse(text, ignoreCase: true, out PanelSortMode sortMode) || !Enum.IsDefined(sortMode))
        {
            return "Error: mode must be one of as_listed, name, extension, size, date.";
        }

        sortable.SetSort(new PanelSort(sortMode, descending && sortMode != PanelSortMode.AsListed));
        return $"{PanelLabel(model)} panel sort: {(sortable.Sort.IsAsListed ? "as listed" : sortable.Sort.Describe())}";
    }

    internal string SetPanelFilter(string text, string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }

        model.Filter = string.IsNullOrEmpty(text) ? null : text;
        return string.IsNullOrEmpty(text)
            ? $"{PanelLabel(model)} panel filter cleared."
            : $"{PanelLabel(model)} panel shows {model.FilteredEntries.Count} entries matching '{text}'.";
    }

    internal string RefreshPanel(string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }

        model.Refresh(true);
        return $"Refreshing {PanelLabel(model)} panel.";
    }

    #endregion

    #region File operations

    internal async Task<string> CreateFolder(string name, string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        if (model.IsReadOnly)
        {
            return "Error: this panel is read-only.";
        }
        if (!IsPlainName(name))
        {
            return "Error: give a plain folder name without path separators.";
        }

        try
        {
            Url target = new(UrlPath.Child(model.Url.FullUrl, name, isFolder: true));
            await model.CreateFolderAsync(target);
            model.Refresh(true);
            return $"Created {target.FullUrl}";
        }
        catch (Exception ex)
        {
            return $"Error: could not create the folder: {ex.Message}";
        }
    }

    internal Task<string> CopyToOtherPanel(string? panel = null, string? names = null) =>
        TransferAsync(panel, names, move: false);

    internal Task<string> MoveToOtherPanel(string? panel = null, string? names = null) =>
        TransferAsync(panel, names, move: true);

    private async Task<string> TransferAsync(string? panel, string? names, bool move)
    {
        if (!TryPanel(panel, out IContentModel source, out string error))
        {
            return error;
        }

        IContentModel? target = source.Name == Content.Left ? Find(Content.Right) : Find(Content.Left);
        if (target?.Url is null)
        {
            return "Error: there is no other panel to transfer into.";
        }
        if (target.IsReadOnly || (move && source.IsReadOnly))
        {
            return "Error: a read-only panel cannot be written to.";
        }
        if (source.Url.ContainerType != target.Url.ContainerType)
        {
            return "Error: copying between different kinds of drives is not supported by the AI tools; use F5.";
        }

        IReadOnlyList<Url> urls = ResolveTargets(source, names, out string problem);
        if (urls.Count == 0)
        {
            return problem;
        }

        string verb = move ? "Moved" : "Copied";
        var done = new List<string>();
        var failed = new List<string>();
        foreach (Url url in urls)
        {
            string name = FocusedEntry.NameOf(url);
            Url destination = new(UrlPath.Child(target.Url.FullUrl, name, url.PathType == PathType.Folder));
            try
            {
                if (target.ExistsFile(destination) || target.ExistsFolder(destination))
                {
                    failed.Add($"{name}: already exists in the target");
                    continue;
                }

                if (move)
                {
                    await source.MoveAsync(url, destination, overwrite: false);
                }
                else
                {
                    await source.CopyAsync(url, destination, overwrite: false);
                }
                done.Add(name);
            }
            catch (Exception ex)
            {
                failed.Add($"{name}: {ex.Message}");
            }
        }

        if (source is IMarkableContentModel marks && done.Count > 0)
        {
            marks.ClearMarks();
        }
        source.Refresh(true);
        target.Refresh(true);
        return Report(verb, done, failed);
    }

    internal async Task<string> RenameEntry(string newName, string? name = null, string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        if (model.IsReadOnly)
        {
            return "Error: this panel is read-only.";
        }
        if (!IsPlainName(newName))
        {
            return "Error: give a plain new name without path separators.";
        }

        IEntry? entry = string.IsNullOrWhiteSpace(name) ? model.GetFocusedEntry() : FindEntry(model, name, out _);
        if (entry?.Url is null || FocusedEntry.IsParentEntry(entry))
        {
            return string.IsNullOrWhiteSpace(name) ? "Nothing renamable is under the cursor." : $"Error: no entry named '{name}'.";
        }

        Url source = entry.Url;
        string parent = source.FullUrl.TrimEnd('/', '\\');
        parent = parent[..(parent.LastIndexOfAny(['/', '\\']) + 1)];
        Url destination = new(UrlPath.Child(parent, newName, source.PathType == PathType.Folder));
        try
        {
            if (model.ExistsFile(destination) || model.ExistsFolder(destination))
            {
                return $"Error: '{newName}' already exists.";
            }

            await model.MoveAsync(source, destination, overwrite: false);
            model.Refresh(true);
            return $"Renamed {FocusedEntry.NameOf(source)} to {newName}.";
        }
        catch (Exception ex)
        {
            return $"Error: could not rename: {ex.Message}";
        }
    }

    internal async Task<string> DeleteEntries(string? panel = null, string? names = null, bool confirmed = false)
    {
        if (!confirmed)
        {
            return "Not deleted: this is destructive. Ask the user to confirm, then call again with confirmed=true.";
        }
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        if (model.IsReadOnly)
        {
            return "Error: this panel is read-only.";
        }

        IReadOnlyList<Url> urls = ResolveTargets(model, names, out string problem);
        if (urls.Count == 0)
        {
            return problem;
        }

        if (approvalService is null)
        {
            return "Error: AI action approval is unavailable; nothing was deleted.";
        }

        string summary = string.Format(
            UiText.Get("Ai.Approval.DeleteSummary", "AI requests moving {0} item(s) from {1} to the recycle bin. Review every target below."),
            urls.Count,
            model.Url.FullUrl);
        AiActionApprovalRequest request = new(summary, urls.Select(url => url.FullUrl).ToList());
        if (!await approvalService.ConfirmAsync(request))
        {
            return "Not deleted: app approval was declined or cancelled.";
        }

        var done = new List<string>();
        var failed = new List<string>();
        foreach (Url url in urls)
        {
            string name = FocusedEntry.NameOf(url);
            try
            {
                // Recursive for a folder: the user confirmed the named entries, and the bin keeps them recoverable.
                model.Delete(url, recursive: url.PathType == PathType.Folder, moveToRecycleBin: true);
                done.Add(name);
            }
            catch (Exception ex)
            {
                failed.Add($"{name}: {ex.Message}");
            }
        }

        if (model is IMarkableContentModel marks && done.Count > 0)
        {
            marks.ClearMarks();
        }
        model.Refresh(true);
        return Report("Deleted (to the recycle bin where there is one)", done, failed);
    }

    #endregion

    #region Helpers

    /// <summary>The explicitly named entries, else the marked ones, else the one under the cursor.</summary>
    private IReadOnlyList<Url> ResolveTargets(IContentModel model, string? names, out string problem)
    {
        problem = string.Empty;
        if (!string.IsNullOrWhiteSpace(names))
        {
            var urls = new List<Url>();
            foreach (string name in names.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                IEntry? entry = FindEntry(model, name, out string notFound);
                if (entry is null)
                {
                    problem = notFound;
                    return [];
                }
                if (FocusedEntry.IsParentEntry(entry) || entry is IDriveObjectEntry)
                {
                    problem = $"Error: '{name}' is not a file or folder.";
                    return [];
                }
                urls.Add(entry.Url);
            }
            return urls;
        }

        IReadOnlyList<Url> actionable = FocusedEntry.ActionableUrls(model);
        if (actionable.Count == 0)
        {
            problem = "Nothing to act on: mark entries, name them, or put the cursor on one.";
        }
        return actionable;
    }

    /// <summary>An entry by exact name, else by unique case-insensitive name or prefix; the reason it was not found otherwise.</summary>
    private static IEntry? FindEntry(IContentModel model, string? name, out string problem)
    {
        problem = string.Empty;
        string wanted = (name ?? string.Empty).Trim().TrimEnd('/', '\\');
        if (wanted.Length == 0)
        {
            problem = "Error: give an entry name.";
            return null;
        }

        List<IEntry> entries = [.. model.FilteredEntries.Where(e => !FocusedEntry.IsParentEntry(e) || wanted == FocusedEntry.ParentEntryName)];
        string NameOf(IEntry e) => (e.DisplayName ?? e.Name ?? string.Empty).Trim().TrimEnd('/', '\\');

        IEntry? exact = entries.FirstOrDefault(e => NameOf(e) == wanted);
        if (exact is not null)
        {
            return exact;
        }

        List<IEntry> loose = [.. entries.Where(e => NameOf(e).Equals(wanted, StringComparison.OrdinalIgnoreCase))];
        if (loose.Count == 0)
        {
            loose = [.. entries.Where(e => NameOf(e).StartsWith(wanted, StringComparison.OrdinalIgnoreCase))];
        }

        if (loose.Count == 1)
        {
            return loose[0];
        }

        problem = loose.Count == 0
            ? $"Error: no entry named '{wanted}' is listed (is a filter set? try list_panel_entries)."
            : $"Error: '{wanted}' is ambiguous: {string.Join(", ", loose.Take(5).Select(NameOf))}.";
        return null;
    }

    private static bool IsPlainName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.IndexOfAny(['/', '\\', '\0']) < 0
        && name is not ("." or "..");

    private static string Report(string verb, List<string> done, List<string> failed)
    {
        var sb = new StringBuilder();
        if (done.Count > 0)
        {
            sb.Append($"{verb} {done.Count}: {string.Join(", ", done.Take(10))}{(done.Count > 10 ? ", ..." : string.Empty)}.");
        }
        if (failed.Count > 0)
        {
            sb.Append($"{(sb.Length > 0 ? " " : string.Empty)}Failed {failed.Count}: {string.Join("; ", failed.Take(5))}.");
        }
        return sb.Length > 0 ? sb.ToString() : "Nothing was done.";
    }

    #endregion
}

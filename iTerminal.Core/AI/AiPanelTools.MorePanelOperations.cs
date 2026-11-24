using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iTerminal.Core.Commands;
using iTerminal.Core.Helper;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Checksums;
using iTerminal.Core.Services.Search;
using Microsoft.Extensions.AI;
using System.Globalization;
using System.Text;

namespace iTerminal.Core.AI;

/// <summary>
/// The remaining file-panel operations as AI tools: jumping to any path or the drive root, showing hidden files,
/// creating an empty file, folder sizes, properties, checksums, finding files and renaming many entries at once.
///
/// <para>Reading tools return metadata only (a hash, a size, a path), never file content, so they need no content
/// approval. Writing tools follow the rule of the others: a preview unless <c>confirmed=true</c>, and then the
/// app-owned approval dialog decides.</para>
/// </summary>
public partial class AiPanelTools
{
    /// <summary>The most paths <c>find_files</c> returns.</summary>
    internal const int MaxFoundFiles = 100;

    private IReadOnlyList<AITool> CreateMorePanelTools() =>
    [
        AIFunctionFactory.Create(GoToPath, "go_to_path",
            "Navigate a panel to a folder path or URL (C:\\Users, /home/me, zip://..., ftp://...). Credentials never go in a URL."),
        AIFunctionFactory.Create(GoToRootFolder, "go_to_root_folder",
            "Navigate a panel to the root of its current drive or container."),
        AIFunctionFactory.Create(SetHiddenFilesVisible, "set_hidden_files_visible",
            "Show (visible=true) or hide hidden files and folders in both panels. Same setting as Ctrl+Shift+S."),
        AIFunctionFactory.Create(CreateFile, "create_file",
            "Create a new empty file in the folder a panel shows. Never overwrites. Without confirmed=true this only previews. With confirmed=true, the app must show an approval dialog and the user must approve there; this argument is not authorization."),
        AIFunctionFactory.Create(CalculateFolderSize, "calculate_folder_size",
            "Add up the size, file count and folder count of a local folder (the one under the cursor when name is empty). Changes nothing."),
        AIFunctionFactory.Create(GetEntryProperties, "get_entry_properties",
            "Show the properties of a local file or folder: size, dates, attributes and link target (the cursor entry when name is empty). Changes nothing."),
        AIFunctionFactory.Create(ComputeChecksum, "compute_checksum",
            "Compute the checksum of a file (the cursor entry when name is empty). algorithm: sha256 (default), sha1, sha512 or md5. Returns only the hash, never the content."),
        AIFunctionFactory.Create(FindFiles, "find_files",
            $"Search the local folder a panel shows for file names matching patterns ('*.log;report', ';' or ',' separated; a word without wildcard matches anywhere in the name). containingText restricts to files that contain it (only paths are returned). minBytes limits to larger files. includeSubfolders defaults to true. Returns at most {MaxFoundFiles} paths. Changes nothing."),
        AIFunctionFactory.Create(MultiRename, "multi_rename",
            "Rename several entries at once by replacing text in their names: every occurrence of find becomes replace (case-sensitive unless ignoreCase=true). Targets are the names given (';'-separated), else the marked entries, else the cursor entry. Never overwrites. Without confirmed=true this only previews the new names. With confirmed=true, the app must show an approval dialog and the user must approve there; this argument is not authorization."),
        AIFunctionFactory.Create(UndoLastRename, "undo_last_rename",
            "Put back the names changed by the last rename or multi_rename (one step). Without confirmed=true this only previews. With confirmed=true, the app must show an approval dialog and the user must approve there; this argument is not authorization."),
    ];

    #region Navigation and view

    internal async Task<string> GoToPath(string path, string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Error: give a folder path or URL.";
        }

        try
        {
            Url url = new(path.Trim());
            await contentModelService.SetUrlAsync(model.Name, url);
            return $"{PanelLabel(model)} panel is now at {url.FullUrl}";
        }
        catch (Exception ex)
        {
            return $"Error: cannot go to '{path}': {ex.Message}";
        }
    }

    internal async Task<string> GoToRootFolder(string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        if (model.Url is null)
        {
            return "Error: the panel shows no folder.";
        }

        try
        {
            Url root = model.Url.RootUrl;
            await contentModelService.SetUrlAsync(model.Name, root);
            return $"{PanelLabel(model)} panel is now at {root.FullUrl}";
        }
        catch (Exception ex)
        {
            return $"Error: cannot go to the root folder: {ex.Message}";
        }
    }

    internal string SetHiddenFilesVisible(bool visible)
    {
        if (settingsService is null)
        {
            return "Error: settings are not available.";
        }

        PanelViewOptions next = PanelViewOptions.Load(settingsService) with { ShowHidden = visible };
        next.Save(settingsService);
        foreach (IContentModel model in contentModelService.ContentModels.AsSnapshot())
        {
            if (model is ISortableContentModel sortable)
            {
                sortable.SetViewOptions(next);
            }
        }

        return visible ? "Hidden files are shown in both panels." : "Hidden files are hidden in both panels.";
    }

    #endregion

    #region Reading metadata

    /// <summary>The entry named <paramref name="name"/>, else the one under the cursor, as a local path.</summary>
    private static bool TryLocalEntry(IContentModel model, string? name, out IEntry entry, out string localPath, out string error)
    {
        entry = null!;
        localPath = string.Empty;
        IEntry? found = string.IsNullOrWhiteSpace(name) ? model.GetFocusedEntry() : FindEntry(model, name, out error);
        if (found is not null) error = string.Empty;
        else error = string.IsNullOrWhiteSpace(name) ? "Nothing is under the cursor." : $"Error: no entry named '{name}' is listed.";
        if (found is null)
        {
            return false;
        }
        if (found.Url is null || FocusedEntry.IsParentEntry(found) || found is IDriveObjectEntry)
        {
            error = "Error: put the cursor on a file or folder, or name one.";
            return false;
        }
        if (found.Url.ContainerType != ContainerType.FileSystem)
        {
            error = "Error: this tool works on local files only.";
            return false;
        }

        entry = found;
        localPath = Path.TrimEndingDirectorySeparator(found.Url.Path);
        return true;
    }

    internal async Task<string> CalculateFolderSize(string? name = null, string? panel = null, CancellationToken cancellationToken = default)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        if (!TryLocalEntry(model, name, out IEntry entry, out string path, out error))
        {
            return error;
        }
        if (entry.Url.PathType != PathType.Folder)
        {
            return $"{entry.DisplayName?.Trim()} is a file: {entry.FileSize?.Trim()}.";
        }

        try
        {
            Services.Properties.FolderMeasure measure = await (propertiesService ?? new Services.Properties.FilePropertiesService())
                .MeasureAsync(path, null, cancellationToken);
            return $"{path}: {measure.Bytes.ToFileSize()} ({measure.Bytes.ToString("N0", CultureInfo.InvariantCulture)} bytes) in {measure.Files} files and {measure.Folders} folders"
                + (measure.Unreadable > 0 ? $"; {measure.Unreadable} entries could not be read." : ".");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Error: could not measure the folder: {ex.Message}";
        }
    }

    internal string GetEntryProperties(string? name = null, string? panel = null)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        if (!TryLocalEntry(model, name, out _, out string path, out error))
        {
            return error;
        }

        try
        {
            Services.Properties.FileProperties p = (propertiesService ?? new Services.Properties.FilePropertiesService()).Read(path);
            var sb = new StringBuilder();
            sb.AppendLine($"Name: {p.Name}");
            sb.AppendLine($"Path: {p.Path}");
            sb.AppendLine($"Type: {(p.IsFolder ? "folder" : "file")}");
            if (p.Size is long size)
            {
                sb.AppendLine($"Size: {size.ToFileSize()} ({size.ToString("N0", CultureInfo.InvariantCulture)} bytes)");
            }
            sb.AppendLine($"Created: {p.Created:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Modified: {p.Modified:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Accessed: {p.Accessed:yyyy-MM-dd HH:mm:ss}");
            sb.Append($"Attributes: {p.Attributes}");
            if (p.UnixMode is { } mode)
            {
                sb.Append($"{Environment.NewLine}Mode: {mode}");
            }
            if (!string.IsNullOrEmpty(p.LinkTarget))
            {
                sb.Append($"{Environment.NewLine}Link target: {p.LinkTarget}");
            }
            return sb.ToString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Error: could not read the properties: {ex.Message}";
        }
    }

    internal async Task<string> ComputeChecksum(string? name = null, string? panel = null, string? algorithm = null, CancellationToken cancellationToken = default)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        if (checksumService is null || fileSystemResolver is null)
        {
            return "Error: checksums are not available.";
        }

        string wanted = (algorithm ?? "sha256").Trim().Replace("-", string.Empty).ToLowerInvariant();
        ChecksumAlgorithm? chosen = wanted switch
        {
            "md5" => ChecksumAlgorithm.Md5,
            "sha1" => ChecksumAlgorithm.Sha1,
            "sha256" => ChecksumAlgorithm.Sha256,
            "sha512" => ChecksumAlgorithm.Sha512,
            _ => null,
        };
        if (chosen is null)
        {
            return "Error: algorithm must be one of sha256, sha1, sha512, md5.";
        }

        IEntry? entry = string.IsNullOrWhiteSpace(name) ? model.GetFocusedEntry() : FindEntry(model, name, out error);
        if (entry?.Url is null || FocusedEntry.IsParentEntry(entry) || entry is IDriveObjectEntry || entry.Url.PathType != PathType.File)
        {
            return string.IsNullOrEmpty(error) ? "Error: put the cursor on a file or name one." : error;
        }

        try
        {
            Url url = new(entry.Url.FullUrl, entry.Url.DisplayName);
            IFileSystem fileSystem = fileSystemResolver.Resolve(url, model, "Checksum");
            ChecksumResult result = await checksumService.ComputeAsync(url, fileSystem, null, cancellationToken);
            return $"{ChecksumService.DisplayName(chosen.Value)} of {entry.DisplayName?.Trim()}: {result.Hashes[chosen.Value]} ({result.Bytes.ToString("N0", CultureInfo.InvariantCulture)} bytes)";
        }
        catch (Exception ex)
        {
            return $"Error: could not compute the checksum: {ex.Message}";
        }
    }

    internal async Task<string> FindFiles(
        string patterns,
        string? panel = null,
        string? containingText = null,
        long minBytes = 0,
        bool includeSubfolders = true,
        CancellationToken cancellationToken = default)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        if (model.Url is null || model.Url.ContainerType != ContainerType.FileSystem)
        {
            return "Error: find_files searches local folders only.";
        }

        string start = model.Url.Path;
        var query = new FileSearchQuery(
            start,
            patterns ?? string.Empty,
            containingText ?? string.Empty,
            includeSubfolders,
            minBytes > 0 ? minBytes : null);

        var found = new List<FileSearchHit>();
        try
        {
            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(30));
            bool timedOut = false;
            await Task.Run(() =>
            {
                try
                {
                    FileSearch.Run(query, hit =>
                    {
                        if (found.Count < MaxFoundFiles + 1)
                        {
                            found.Add(hit);
                        }
                        if (found.Count > MaxFoundFiles)
                        {
                            limit.Cancel();
                        }
                    }, null, limit.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    timedOut = found.Count <= MaxFoundFiles;
                }
            }, cancellationToken);

            if (found.Count == 0)
            {
                return timedOut ? "Nothing found before the 30 s limit." : "Nothing found.";
            }

            var sb = new StringBuilder();
            foreach (FileSearchHit hit in found.Take(MaxFoundFiles))
            {
                sb.AppendLine($"{hit.Path}{(hit.IsFolder ? "/" : string.Empty)}  {(hit.IsFolder ? string.Empty : hit.Size.ToFileSize())}".TrimEnd());
            }
            if (found.Count > MaxFoundFiles)
            {
                sb.AppendLine($"... more than {MaxFoundFiles} matches; narrow the patterns.");
            }
            else if (timedOut)
            {
                sb.AppendLine("... stopped at the 30 s limit.");
            }
            return sb.ToString().TrimEnd();
        }
        catch (DirectoryNotFoundException ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    #endregion

    #region Writing

    internal async Task<string> CreateFile(string name, string? panel = null, bool confirmed = false)
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
            return "Error: give a plain file name without path separators.";
        }

        try
        {
            Url target = new(UrlPath.Child(model.Url.FullUrl, name, isFolder: false));
            if (model.ExistsFile(target) || model.ExistsFolder(target))
            {
                return $"Error: '{name}' already exists.";
            }

            string? refusal = await RequestMutationApprovalAsync(
                $"creating an empty file in {model.Url.FullUrl}",
                [$"Create file: {target.FullUrl}"],
                confirmed);
            if (refusal is not null)
            {
                return refusal;
            }

            await model.WriteFileAsync(target, new MemoryStream());
            model.Refresh(true);
            return $"Created {target.FullUrl}";
        }
        catch (Exception ex)
        {
            return $"Error: could not create the file: {ex.Message}";
        }
    }

    internal async Task<string> MultiRename(string find, string replace, string? panel = null, string? names = null, bool ignoreCase = false, bool confirmed = false)
    {
        if (!TryPanel(panel, out IContentModel model, out string error))
        {
            return error;
        }
        if (model.IsReadOnly)
        {
            return "Error: this panel is read-only.";
        }
        if (string.IsNullOrEmpty(find))
        {
            return "Error: give the text to find in the names.";
        }

        IReadOnlyList<Url> urls = ResolveTargets(model, names, out string problem);
        if (urls.Count == 0)
        {
            return problem;
        }

        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var plan = new List<(Url Source, Url Target, string OldName, string NewName)>();
        var skipped = new List<string>();
        var newNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Url source in urls)
        {
            string oldName = FocusedEntry.NameOf(source);
            string newName = oldName.Replace(find, replace ?? string.Empty, comparison);
            if (newName == oldName)
            {
                continue;
            }
            if (!IsPlainName(newName))
            {
                skipped.Add($"{oldName}: '{newName}' is not a valid name");
                continue;
            }
            if (!newNames.Add(newName))
            {
                skipped.Add($"{oldName}: another entry would get the name '{newName}'");
                continue;
            }

            string parent = source.FullUrl.TrimEnd('/', '\\');
            parent = parent[..(parent.LastIndexOfAny(['/', '\\']) + 1)];
            Url destination = new(UrlPath.Child(parent, newName, source.PathType == PathType.Folder));
            // A change of case only (Report.txt to report.txt) finds the entry itself on a case-insensitive file system, so
            // it is not a clash. Where a different entry already has the name, the move itself refuses to overwrite it.
            bool caseOnly = string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly && (model.ExistsFile(destination) || model.ExistsFolder(destination)))
            {
                skipped.Add($"{oldName}: '{newName}' already exists");
                continue;
            }
            plan.Add((source, destination, oldName, newName));
        }

        if (plan.Count == 0)
        {
            return "Nothing to rename" + (skipped.Count > 0 ? ": " + string.Join("; ", skipped.Take(5)) + "." : ": no name contains the text.");
        }

        string? refusal = await RequestMutationApprovalAsync(
            $"renaming {plan.Count} entries in {model.Url.FullUrl}",
            [.. plan.Select(step => $"Rename {step.OldName} to {step.NewName}")],
            confirmed);
        if (refusal is not null)
        {
            return skipped.Count > 0 ? $"{refusal}{Environment.NewLine}Skipped: {string.Join("; ", skipped.Take(5))}." : refusal;
        }

        var done = new List<string>();
        var failed = new List<string>();
        var renamed = new List<(Url Source, Url Target)>();
        foreach ((Url source, Url target, string oldName, string newName) in plan)
        {
            try
            {
                await model.MoveAsync(source, target, overwrite: false);
                done.Add($"{oldName} -> {newName}");
                renamed.Add((source, target));
            }
            catch (Exception ex)
            {
                failed.Add($"{oldName}: {ex.Message}");
            }
        }

        renameUndoService?.RememberBatch(model, renamed);
        failed.AddRange(skipped);
        if (model is IMarkableContentModel marks && done.Count > 0)
        {
            marks.ClearMarks();
        }
        model.Refresh(true);
        return Report("Renamed", done, failed);
    }

    internal async Task<string> UndoLastRename(bool confirmed = false)
    {
        if (renameUndoService is null)
        {
            return "Error: rename undo is not available.";
        }

        Services.RenameUndoService.Rename? first = renameUndoService.Peek();
        if (first is null)
        {
            return "There is no rename to undo.";
        }

        string? refusal = await RequestMutationApprovalAsync(
            "undoing the last rename",
            [$"Rename {FocusedEntry.NameOf(first.Target)} back to {FocusedEntry.NameOf(first.Source)} (and the rest of that batch)"],
            confirmed);
        if (refusal is not null)
        {
            return refusal;
        }

        var done = new List<string>();
        var failed = new List<string>();
        foreach (Services.RenameUndoService.Rename step in renameUndoService.TakeAll())
        {
            string name = FocusedEntry.NameOf(step.Source);
            try
            {
                if (!step.Model.ExistsFile(step.Target) && !step.Model.ExistsFolder(step.Target))
                {
                    failed.Add($"{name}: the renamed entry is gone");
                }
                else if (step.Model.ExistsFile(step.Source) || step.Model.ExistsFolder(step.Source))
                {
                    failed.Add($"{name}: the old name is taken");
                }
                else
                {
                    await step.Model.MoveAsync(step.Target, step.Source, overwrite: false);
                    done.Add(name);
                }
            }
            catch (Exception ex)
            {
                failed.Add($"{name}: {ex.Message}");
            }
            step.Model.Refresh(true);
        }

        return Report("Restored", done, failed);
    }

    #endregion
}

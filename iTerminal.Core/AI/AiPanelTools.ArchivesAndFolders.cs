using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iTerminal.Core.Commands;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Helper;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Services.Compare;
using iTerminal.Core.Tasks;
using Microsoft.Extensions.AI;
using System.Text;

namespace iTerminal.Core.AI;

/// <summary>
/// The archive tools (compress, extract) and the folder tools (compare, synchronise) of <see cref="AiPanelTools"/>.
///
/// <para>They run the same tasks the dialogs do (<see cref="CompressTask"/>, <see cref="ExtractTask"/>,
/// <see cref="FolderSyncTask"/>) to completion on the calling background thread, so the result is known when the
/// tool returns. There is no progress window.</para>
///
/// <para>Every write is a preview unless <c>confirmed=true</c>; the app-owned approval dialog still requires the user's explicit approval.</para>
/// </summary>
public partial class AiPanelTools
{
    /// <summary>The most differences or plan steps a tool lists back.</summary>
    internal const int MaxListedDifferences = 100;

    private IReadOnlyList<AITool> CreateArchiveAndFolderTools() =>
    [
        AIFunctionFactory.Create(CompressEntries, "compress_entries",
            "Pack the marked entries of a panel (or the one under the cursor; or the names given, ';'-separated) into a new archive in the other panel's folder (or the same folder when intoOtherPanel=false). format: zip (default), 7z, tar.gz, tar.bz2 or tar; the archive name gets the right ending. Refuses to touch an archive that already exists unless addToExisting=true. Without confirmed=true this only previews. With confirmed=true, the app must show an approval dialog and the user must approve there; this argument is not authorization."),
        AIFunctionFactory.Create(ExtractArchives, "extract_archives",
            "Unpack the marked archives of a panel (or the one under the cursor; or the names given, ';'-separated) into the other panel's folder (or the same folder when intoOtherPanel=false). Each archive goes into a folder of its own unless eachIntoOwnFolder=false. Existing files are kept unless overwrite=true. Password-protected archives are reported, not opened. Without confirmed=true this only previews. With confirmed=true, the app must show an approval dialog and the user must approve there; this argument is not authorization."),
        AIFunctionFactory.Create(ListArchiveFormats, "list_archive_formats",
            "List the archive formats compress_entries can write on this machine, and the formats extract_archives can read."),
        AIFunctionFactory.Create(CompareFolders, "compare_panel_folders",
            "Compare the folders shown in the left and right panels. mode: size_and_date (default), size_only or content (reads every file). includeSubfolders defaults to true. Lists the differences; changes nothing."),
        AIFunctionFactory.Create(SynchronizeFolders, "synchronize_panel_folders",
            "Synchronise the left and right panel folders. direction: both (default), left_to_right or right_to_left. deleteExtras removes what only the target has (one-way only, to the recycle bin). Without confirmed=true this only previews the plan. After the user agrees in chat, confirmed=true shows an app approval dialog with the full plan; the user must approve there."),
    ];

    #region Archives

    internal Task<string> ListArchiveFormats()
    {
        if (compressionService is null || extractionService is null)
        {
            return Task.FromResult("Error: archive support is not available.");
        }

        var sb = new StringBuilder("Can write: ");
        sb.AppendLine(string.Join(", ", compressionService.AvailableFormats.Select(f => $"{f.Name} ({f.Extension})")));
        string[] missing = [.. ArchiveFormat.All.Where(f => !compressionService.AvailableFormats.Contains(f))
            .Select(f => $"{f.Name}: {compressionService.WhyUnavailable(f)}")];
        if (missing.Length > 0)
        {
            sb.AppendLine("Cannot write here: " + string.Join("; ", missing));
        }
        sb.Append("Can read: ZIP, TAR, TAR.GZ, TAR.BZ2, GZ, BZ2 and, with 7-Zip installed, 7z, RAR, XZ, ISO, CAB and more.");
        return Task.FromResult(sb.ToString());
    }

    internal async Task<string> CompressEntries(
        string archiveName,
        string? panel = null,
        string? names = null,
        string? format = null,
        bool intoOtherPanel = true,
        bool addToExisting = false,
        bool confirmed = false)
    {
        if (compressionService is null || fileSystemResolver is null)
        {
            return "Error: archive support is not available.";
        }
        if (!TryPanel(panel, out IContentModel source, out string error))
        {
            return error;
        }
        if (!IsPlainName(archiveName))
        {
            return "Error: give a plain archive name without path separators.";
        }

        ArchiveFormat? chosen = ParseFormat(format, archiveName);
        if (chosen is null)
        {
            return "Error: format must be one of zip, 7z, tar.gz, tar.bz2, tar.";
        }
        if (!compressionService.AvailableFormats.Contains(chosen))
        {
            return $"Error: {chosen.Name} cannot be written here: {compressionService.WhyUnavailable(chosen)}";
        }

        IContentModel folderModel = intoOtherPanel ? OtherOf(source) ?? source : source;
        if (folderModel.IsReadOnly)
        {
            return "Error: the archive's folder is read-only.";
        }

        IReadOnlyList<Url> sources = ResolveTargets(source, names, out string problem);
        if (sources.Count == 0)
        {
            return problem;
        }

        string finalName = chosen.ApplyExtension(archiveName);
        Url archiveUrl = new(UrlPath.Child(folderModel.Url.FullUrl, finalName, isFolder: false));
        IFileSystem archiveSystem = fileSystemResolver.Resolve(archiveUrl, folderModel, "Compress");
        bool archiveExists = archiveSystem.ExistsFile(archiveUrl);
        if (archiveExists && !addToExisting)
        {
            return $"Error: {finalName} already exists; choose another name or pass addToExisting=true.";
        }
        if (sources.Any(s => s.FullUrl == archiveUrl.FullUrl))
        {
            return "Error: the archive cannot contain itself.";
        }

        string operation = archiveExists
            ? $"adding {sources.Count} item(s) to existing archive {archiveUrl.FullUrl}"
            : $"creating archive {archiveUrl.FullUrl} from {sources.Count} item(s)";
        List<string> actions = sources.Select(sourceUrl => $"Add {sourceUrl.FullUrl} to {archiveUrl.FullUrl}").ToList();
        string? refusal = await RequestMutationApprovalAsync(operation, actions, confirmed);
        if (refusal is not null)
        {
            return refusal;
        }

        if (!archiveExists && archiveSystem.ExistsFile(archiveUrl))
        {
            return $"Error: {finalName} appeared while approval was pending; nothing was compressed. Review the plan again.";
        }

        var task = new CompressTask(
            compressionService,
            sources,
            url => fileSystemResolver.Resolve(url, source, "Compress"),
            archiveUrl,
            new CompressionOptions(chosen, chosen.DefaultLevel));
        try
        {
            await task.RunAsync();
        }
        catch (Exception ex)
        {
            return $"Error: could not compress: {ex.Message}";
        }
        finally
        {
            folderModel.Refresh(true);
        }

        if (task.Error is not null)
        {
            return $"Error: could not compress: {task.Error}";
        }
        if (task.Result is { } result)
        {
            if (source is Model.ContentModels.IMarkableContentModel marks)
            {
                marks.ClearMarks();
            }
            return $"Created {finalName} in {folderModel.Url.FullUrl} with {result.Added} entries"
                + (result.Kept > 0 ? $", kept {result.Kept} already in it" : string.Empty)
                + (result.Skipped > 0 ? $", skipped {result.Skipped} that were already there" : string.Empty) + ".";
        }
        return "Nothing was compressed (cancelled?).";
    }

    internal async Task<string> ExtractArchives(
        string? panel = null,
        string? names = null,
        bool intoOtherPanel = true,
        bool eachIntoOwnFolder = true,
        bool overwrite = false,
        bool confirmed = false)
    {
        if (extractionService is null || fileSystemResolver is null)
        {
            return "Error: archive support is not available.";
        }
        if (!TryPanel(panel, out IContentModel source, out string error))
        {
            return error;
        }

        IReadOnlyList<Url> chosen = ResolveTargets(source, names, out string problem);
        if (chosen.Count == 0)
        {
            return problem;
        }

        var archives = new List<Url>();
        var refused = new List<string>();
        foreach (Url url in chosen)
        {
            string name = FocusedEntry.NameOf(url);
            if (url.PathType == PathType.Folder || !extractionService.IsArchive(name))
            {
                refused.Add($"{name}: not an archive");
            }
            else if (extractionService.WhyCannotExtract(name) is { } why)
            {
                refused.Add($"{name}: {why}");
            }
            else
            {
                archives.Add(url);
            }
        }
        if (archives.Count == 0)
        {
            return "Nothing to extract. " + string.Join("; ", refused);
        }

        IContentModel targetModel = intoOtherPanel ? OtherOf(source) ?? source : source;
        if (targetModel.IsReadOnly)
        {
            return "Error: the target folder is read-only.";
        }

        Url targetFolder = targetModel.Url;
        List<string> actions = archives.Select(archive =>
        {
            string archiveName = FocusedEntry.NameOf(archive);
            Url destination = ExtractTask.DestinationFor(
                targetFolder,
                extractionService.FolderNameFor(archiveName),
                eachIntoOwnFolder);
            return $"Extract {archive.FullUrl} into {destination.FullUrl} (overwrite existing files: {(overwrite ? "yes" : "no")})";
        }).ToList();
        string? refusal = await RequestMutationApprovalAsync(
            $"extracting {archives.Count} archive(s) into {targetFolder.FullUrl}",
            actions,
            confirmed);
        if (refusal is not null)
        {
            return refusal;
        }

        var task = new ExtractTask(
            extractionService,
            archives,
            url => fileSystemResolver.Resolve(url, source, "Extract"),
            targetFolder,
            new ExtractionOptions(Password: null, Overwrite: overwrite),
            eachIntoOwnFolder);
        try
        {
            await task.RunAsync();
        }
        catch (Exception ex)
        {
            return $"Error: could not extract: {ex.Message}";
        }
        finally
        {
            targetModel.Refresh(true);
        }

        ExtractionResult result = task.Result;
        var sb = new StringBuilder($"Extracted {result.Extracted} files from {archives.Count - task.Failures.Count} of {archives.Count} archives into {targetModel.Url.FullUrl}.");
        if (result.Skipped > 0)
        {
            sb.Append($" Skipped {result.Skipped} that already existed (pass overwrite=true to replace).");
        }
        if (result.Unsafe > 0)
        {
            sb.Append($" Refused {result.Unsafe} entries whose path led outside the target folder.");
        }
        if (task.Failures.Count > 0)
        {
            sb.Append(" Failed: " + string.Join("; ", task.Failures.Take(5)) + ".");
        }
        if (refused.Count > 0)
        {
            sb.Append(" Ignored: " + string.Join("; ", refused.Take(5)) + ".");
        }
        return sb.ToString();
    }

    private static ArchiveFormat? ParseFormat(string? format, string archiveName)
    {
        string text = (format ?? string.Empty).Trim().ToLowerInvariant().TrimStart('.');
        if (text.Length == 0)
        {
            return ArchiveFormat.FromFileName(archiveName) ?? ArchiveFormat.Zip;
        }

        return text switch
        {
            "zip" => ArchiveFormat.Zip,
            "7z" or "7zip" or "sevenzip" => ArchiveFormat.SevenZip,
            "tar.gz" or "tgz" or "targz" or "gz" => ArchiveFormat.TarGz,
            "tar.bz2" or "tbz2" or "tarbz2" or "bz2" => ArchiveFormat.TarBz2,
            "tar" => ArchiveFormat.Tar,
            _ => null,
        };
    }

    private IContentModel? OtherOf(IContentModel model) =>
        Find(model.Name == Content.Left ? Content.Right : Content.Left);

    #endregion

    #region Compare and synchronise

    internal async Task<string> CompareFolders(string? mode = null, bool includeSubfolders = true)
    {
        if (!TryFolderPair(out PanelPair pair, out string error))
        {
            return error;
        }
        if (!TryCompareMode(mode, out CompareMode compareMode))
        {
            return "Error: mode must be one of size_and_date, size_only, content.";
        }

        CompareResult result;
        try
        {
            result = await FolderComparer.CompareAsync(
                pair.LeftUrl, pair.LeftSystem, pair.RightUrl, pair.RightSystem,
                new CompareOptions(compareMode, includeSubfolders), progress: null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            return $"Error: could not compare: {ex.Message}";
        }

        var differences = result.Differences.Where(d => !d.IsFolder).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"Left: {pair.LeftUrl.FullUrl}");
        sb.AppendLine($"Right: {pair.RightUrl.FullUrl}");
        sb.AppendLine(result.Summary());
        foreach (DiffItem item in differences.Take(MaxListedDifferences))
        {
            sb.AppendLine($"{DescribeKind(item.Kind)}: {item.Path}");
        }
        if (differences.Count > MaxListedDifferences)
        {
            sb.AppendLine($"... {differences.Count - MaxListedDifferences} more not shown.");
        }
        return sb.ToString().TrimEnd();
    }

    internal async Task<string> SynchronizeFolders(
        string? direction = null,
        bool deleteExtras = false,
        bool includeSubfolders = true,
        string? mode = null,
        bool confirmed = false)
    {
        if (!TryFolderPair(out PanelPair pair, out string error))
        {
            return error;
        }
        if (!TryCompareMode(mode, out CompareMode compareMode))
        {
            return "Error: mode must be one of size_and_date, size_only, content.";
        }

        SyncDirection syncDirection;
        switch ((direction ?? "both").Trim().ToLowerInvariant().Replace("-", "_"))
        {
            case "both":
                syncDirection = SyncDirection.Both;
                break;
            case "left_to_right":
                syncDirection = SyncDirection.LeftToRight;
                break;
            case "right_to_left":
                syncDirection = SyncDirection.RightToLeft;
                break;
            default:
                return "Error: direction must be one of both, left_to_right, right_to_left.";
        }

        SyncPlan plan;
        try
        {
            CompareResult result = await FolderComparer.CompareAsync(
                pair.LeftUrl, pair.LeftSystem, pair.RightUrl, pair.RightSystem,
                new CompareOptions(compareMode, includeSubfolders), progress: null, CancellationToken.None);
            plan = FolderSynchronizer.Plan(result, new SyncOptions(syncDirection, deleteExtras));
        }
        catch (Exception ex)
        {
            return $"Error: could not compare: {ex.Message}";
        }

        if (plan.IsEmpty)
        {
            return plan.Summary();
        }

        if (!confirmed)
        {
            var preview = new StringBuilder();
            preview.AppendLine($"Preview, nothing changed: {plan.Summary()}");
            foreach (SyncAction action in plan.Actions.Where(a => !a.Item.IsFolder).Take(MaxListedDifferences))
            {
                preview.AppendLine(action.Describe());
            }
            if (plan.Actions.Count > MaxListedDifferences)
            {
                preview.AppendLine($"... and more (up to {plan.Actions.Count} steps).");
            }
            preview.Append("Ask the user to confirm, then call again with confirmed=true.");
            return preview.ToString();
        }

        if (approvalService is null)
        {
            return "Error: AI action approval is unavailable; nothing was synchronised.";
        }

        string summary = string.Format(
            UiText.Get("Ai.Approval.SyncSummary", "AI requests synchronising {0} with {1}. Review the full plan below."),
            pair.LeftUrl.FullUrl,
            pair.RightUrl.FullUrl);
        AiActionApprovalRequest request = new(summary, plan.Actions.Select(action => action.Describe()).ToList());
        if (!await approvalService.ConfirmAsync(request))
        {
            return "Not synchronised: app approval was declined or cancelled.";
        }

        var task = new FolderSyncTask(plan, pair.LeftUrl, pair.LeftSystem, pair.RightUrl, pair.RightSystem);
        try
        {
            await task.RunAsync();
        }
        catch (Exception ex)
        {
            return $"Error: could not synchronise: {ex.Message}";
        }
        finally
        {
            pair.Left.Refresh(true);
            pair.Right.Refresh(true);
        }

        var sb = new StringBuilder($"Synchronised: copied {task.Copied}, deleted {task.Deleted}.");
        if (task.Failures.Count > 0)
        {
            sb.Append($" Failed {task.Failures.Count}: {string.Join("; ", task.Failures.Take(5))}.");
        }
        return sb.ToString();
    }

    private sealed record PanelPair(IContentModel Left, IContentModel Right, Url LeftUrl, Url RightUrl, IFileSystem LeftSystem, IFileSystem RightSystem);

    private bool TryFolderPair(out PanelPair pair, out string error)
    {
        pair = null!;
        IContentModel? left = Find(Content.Left);
        IContentModel? right = Find(Content.Right);
        if (left?.Url is null || right?.Url is null)
        {
            error = "Error: both panels must be showing a folder.";
            return false;
        }
        if (fileSystemResolver is null)
        {
            error = "Error: folder comparison is not available.";
            return false;
        }

        try
        {
            pair = new PanelPair(left, right, left.Url, right.Url,
                fileSystemResolver.Resolve(left.Url, left, "Compare"), fileSystemResolver.Resolve(right.Url, right, "Compare"));
        }
        catch (Exception ex)
        {
            error = $"Error: {ex.Message}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryCompareMode(string? mode, out CompareMode result)
    {
        result = CompareMode.SizeAndDate;
        string text = (mode ?? string.Empty).Trim().Replace("_", string.Empty).Replace(" ", string.Empty);
        return text.Length == 0 || Enum.TryParse(text, ignoreCase: true, out result) && Enum.IsDefined(result);
    }

    private static string DescribeKind(DiffKind kind) => kind switch
    {
        DiffKind.LeftOnly => "only on the left",
        DiffKind.RightOnly => "only on the right",
        DiffKind.LeftNewer => "newer on the left",
        DiffKind.RightNewer => "newer on the right",
        DiffKind.Different => "different",
        _ => "identical",
    };

    #endregion
}

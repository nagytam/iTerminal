using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Checksums;

namespace iTerminal.Core.Commands;

/// <summary>
/// Shift+Alt+F3: create a hash manifest for the marked rows (or the whole folder), or verify the folder
/// against one (<see cref="HashManifestDialog"/>). The drive's <see cref="IFileSystem"/> is resolved
/// here, while the panel still has focus, and handed to the dialog; nothing is read before it opens.
/// </summary>
public class HashManifestDialogCommand(
    HashManifestDialog dialog,
    IContentModelService contentModelService,
    FileSystemResolver? fileSystemResolver = null)
    : ShowDialogCommand<HashManifestDialog>(dialog)
{
    private static readonly string[] ChecksumExtensions = [".sha256", ".sha512", ".sha1", ".md5"];

    public override string Title => "Hash manifest";
    public override string Description => "Create a SHA256SUMS-style manifest for the marked rows or the whole folder, or verify the folder against one.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F3, true, true, false);

    /// <summary>The panel the key acts on — the one the menu was opened over, when it runs from the menu.</summary>
    private IContentModel? SourceModel =>
        contentModelService?.GetFocusedModel() ?? contentModelService?.GetLastContentModel();

    public override bool CanExecute() => SourceModel is { Url: not null } model && model is Model.ContentModels.IMarkableContentModel { CanMark: true };

    protected override void OnBeforeShow(HashManifestDialog dialog)
    {
        IContentModel? model = SourceModel;
        if (model?.Url is not { } folder)
        {
            return;
        }

        IReadOnlyList<Url> roots = FocusedEntry.ActionableUrls(model);
        Url? manifest = roots.Count == 1 && roots[0].PathType == PathType.File && IsManifestFile(FocusedEntry.NameOf(roots[0])) ? roots[0] : null;

        IFileSystem? fileSystem = null;
        try
        {
            fileSystem = fileSystemResolver?.Resolve(folder, model, operation: "Hash manifest");
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            // The dialog says so itself, when the read fails for want of a file system.
        }

        // A cursor on a manifest means "check this one": it is not a file to be listed in a new manifest.
        dialog.Load(folder, manifest is null ? roots : [], manifest, fileSystem);
    }

    private static bool IsManifestFile(string name) =>
        HashManifest.IsManifestName(name) || ChecksumExtensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
}

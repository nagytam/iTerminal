using iConsole.Localization;
using iConsole.Commands;
using iConsole.Interfaces;
using iTerminal.Core.Commands;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.FileAssociations;

namespace iTerminal.Core.Controls.Contents;

/// <summary>One row of the panel's right-click menu: what it says and what choosing it does.</summary>
public sealed record ContextMenuItem(string Label, Func<Task> Run);

/// <summary>
/// What the right-click menu of a panel row offers, worked out from what the panel can already do —
/// nothing here is a feature of its own, which is why the menu can never disagree with the keys.
///
/// <list type="bullet">
/// <item><b>Open</b> is Enter on the row, so a folder is entered, an archive opened and a file run
/// (or opened with the program the user chose for it) exactly as the key would.</item>
/// <item><b>The programs for this type of file</b> come from the file associations: "Open with
/// &lt;name&gt;" for each program that matches the file's name, the user's default first, and the same
/// for editors. Local files only — a program is handed a path.</item>
/// <item><b>The rest are the panel's own commands</b>, listed with the key each is bound to, and
/// only those that can run on this row now (<see cref="ICommand.CanExecute"/>): a link is not offered
/// on a file inside an archive, Extract only on an archive.</item>
/// </list>
/// Nothing is offered on the ".." row, on a row that stands for a container, or on the music drives,
/// whose rows have their own keys.
/// </summary>
public static class FileContextMenu
{
    /// <summary>How many programs of each kind are listed by name; the "…with…" dialog has the rest.</summary>
    internal const int MaxPrograms = 3;

    public static IReadOnlyList<ContextMenuItem> Build(
        IContentModel? model,
        IEnumerable<ICommand> commands,
        FileAssociationRunner? associations,
        Func<Task> open)
    {
        var entry = model?.GetFocusedEntry();
        if (model is null
            || entry is null
            || model is ContentModelMusicBase
            || FocusedEntry.IsParentEntry(entry)
            || entry is IDriveObjectEntry)
        {
            return [];
        }

        var snapshot = commands.ToList();
        var items = new List<(string Title, string Key, Func<Task> Run)>
        {
            ("Open", "Enter", open),
        };

        var path = FileAssociationRunner.LocalFilePath(entry.Url);
        if (associations is not null && path is not null)
        {
            var name = Path.GetFileName(path);
            foreach (var program in associations.Associations.GetFor(name, FileAssociationKind.Open).Take(MaxPrograms))
            {
                var chosen = program;
                items.Add((string.Format(UiText.Get("Ctx.OpenWith", "Open with {0}"), chosen.Name), string.Empty, () => associations.Start(chosen, path)));
            }

            foreach (var program in associations.Associations.GetFor(name, FileAssociationKind.Edit).Take(MaxPrograms))
            {
                var chosen = program;
                items.Add((string.Format(UiText.Get("Ctx.EditWith", "Edit with {0}"), chosen.Name), string.Empty, () => associations.Start(chosen, path)));
            }
        }

        // In the order a person scans a menu: look at it, change it, move it, pack it, know about it.
        AddCommands<ViewFileDialogCommand>(items, snapshot, UiText.Get("Ctx.View", "View"));
        AddCommands<EditTextFileDialogCommand>(items, snapshot, UiText.Get("Ctx.Edit", "Edit"));
        AddCommands<OpenWithDialogCommand>(items, snapshot, null);
        AddCommands<DiffFilesDialogCommand>(items, snapshot, UiText.Get("Ctx.CompareFiles", "Compare files"));
        AddCommands<CopyDialogCommand>(items, snapshot, UiText.Get("Ctx.Copy", "Copy"));
        AddCommands<MoveDialogCommand>(items, snapshot, UiText.Get("Ctx.Move", "Move"));
        AddCommands<DeleteDialogCommand>(items, snapshot, UiText.Get("Ctx.Delete", "Delete"));
        AddCommands<CompressDialogCommand>(items, snapshot, UiText.Get("Ctx.Compress", "Compress"));
        AddCommands<ExtractDialogCommand>(items, snapshot, UiText.Get("Ctx.Extract", "Extract"));
        AddCommands<CreateLinkDialogCommand>(items, snapshot, UiText.Get("Ctx.CreateLink", "Create link"));
        AddCommands<EditLinkDialogCommand>(items, snapshot, UiText.Get("Ctx.EditLink", "Edit link"));
        AddCommands<ChecksumDialogCommand>(items, snapshot, UiText.Get("Ctx.Checksums", "Checksums"));
        AddCommands<HashManifestDialogCommand>(items, snapshot, UiText.Get("Ctx.HashManifest", "Hash manifest"));
        AddCommands<SignatureDialogCommand>(items, snapshot, UiText.Get("Ctx.Signatures", "Signatures"));
        AddPrintCommand(items, snapshot);
        AddCommands<FileEncryptionDialogCommand>(items, snapshot, UiText.Get("Ctx.EncryptDecrypt", "Encrypt / decrypt"));
        AddCommands<SecureWipeDialogCommand>(items, snapshot, UiText.Get("Ctx.SecureWipe", "Secure wipe"));
        AddCommands<StreamsDialogCommand>(items, snapshot, UiText.Get("Ctx.StreamsAndAttributes", "Streams and attributes"));
        AddCommands<ScanWithDefenderCommand>(items, snapshot, UiText.Get("Ctx.ScanWithMicrosoftDefender", "Scan with Microsoft Defender"));
        AddCommands<GitPullRebaseCommand>(items, snapshot, null);
        AddCommands<GitSyncCommand>(items, snapshot, null);
        AddCommands<PropertiesDialogCommand>(items, snapshot, UiText.Get("Ctx.Properties", "Properties"));

        // A panel's commands are initialised more than once as its model changes, so the same
        // command can be in the list several times: one row each is enough.
        items = [.. items.DistinctBy(i => (i.Title, i.Key))];

        var titleWidth = items.Max(i => i.Title.Length);
        return [.. items.Select(i => new ContextMenuItem(
            i.Key.Length == 0 ? i.Title : i.Title.PadRight(titleWidth + 2) + i.Key,
            i.Run))];
    }

    /// <summary>
    /// Print always runs (without a file it is the queue manager), so the usual "can it run" test would list it on a folder
    /// or an archive row; the menu offers it only on a local file, and Ctrl+P, the File menu and the palette keep the rest.
    /// </summary>
    private static void AddPrintCommand(List<(string Title, string Key, Func<Task> Run)> items, List<ICommand> commands)
    {
        foreach (var command in commands.OfType<PrintDialogCommand>().Where(c => c.HasPrintableFile))
        {
            var run = command;
            items.Add((UiText.Get("Menu.Print", "Print"), command.KeyDisplayText, () => run.RunAsync(null)));
        }
    }

    private static void AddCommands<TCommand>(
        List<(string Title, string Key, Func<Task> Run)> items,
        List<ICommand> commands,
        string? title)
        where TCommand : ICommand
    {
        foreach (var command in commands.OfType<TCommand>())
        {
            bool canExecute;
            try
            {
                canExecute = command.CanExecute();
            }
            catch
            {
                canExecute = false;
            }

            if (!canExecute)
            {
                continue;
            }

            var run = command;
            items.Add((title ?? command.Title, command.KeyDisplayText, () => run.RunAsync(null)));
        }
    }
}

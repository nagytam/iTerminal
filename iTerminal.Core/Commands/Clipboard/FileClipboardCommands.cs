using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services.Clipboard;

namespace iTerminal.Core.Commands.Clipboard;

/// <summary>
/// Shared gate for the clipboard commands. They are attached to every panel and a key the focused
/// control declines is offered to all of them, so — like the music and marking commands — a command
/// built for a panel acts only while that panel's model is the focused one.
/// </summary>
public abstract class FileClipboardCommand(
    FileClipboardOperations operations,
    IContentModelService contentModelService,
    IContentModel? ownModel = null) : Command
{
    protected FileClipboardOperations Operations => operations;

    /// <summary>The panel the key acts on; null when this command's own panel is not the focused one.</summary>
    protected IContentModel? TargetModel
    {
        get
        {
            var focused = contentModelService.GetFocusedModel();
            if (ownModel is not null)
            {
                return ReferenceEquals(focused, ownModel) ? focused : null;
            }

            return focused ?? contentModelService.GetLastContentModel();
        }
    }
}

/// <summary>Ctrl+C: the marked rows, or the one under the cursor, onto the system clipboard to be copied.</summary>
public class ClipboardCopyCommand(
    FileClipboardOperations operations,
    IContentModelService contentModelService,
    IContentModel? ownModel = null,
    Key? key = null)
    : FileClipboardCommand(operations, contentModelService, ownModel)
{
    public override string Title => "Copy to clipboard";
    public override string Description => "Put the marked files and folders, or the one under the cursor, on the system clipboard, to paste in this app or another program.";

    public override Key? Key { get; init; } = key ?? new Key(ConsoleKey.C, false, false, true);

    public override bool CanExecute() => Operations.LocalPathsOf(TargetModel).Count > 0;

    public override Task ExecuteAsync(object? args) => Operations.CopyAsync(TargetModel, cut: false);
}

/// <summary>Ctrl+X: as <see cref="ClipboardCopyCommand"/>, but the files are moved when they are pasted.</summary>
public class ClipboardCutCommand(
    FileClipboardOperations operations,
    IContentModelService contentModelService,
    IContentModel? ownModel = null,
    Key? key = null)
    : FileClipboardCommand(operations, contentModelService, ownModel)
{
    public override string Title => "Cut to clipboard";
    public override string Description => "Put the marked files and folders, or the one under the cursor, on the system clipboard, to be moved where they are pasted.";

    public override Key? Key { get; init; } = key ?? new Key(ConsoleKey.X, false, false, true);

    public override bool CanExecute() => Operations.LocalPathsOf(TargetModel).Count > 0;

    public override Task ExecuteAsync(object? args) => Operations.CopyAsync(TargetModel, cut: true);
}

/// <summary>Ctrl+V: the clipboard's files into the folder this panel shows.</summary>
public class ClipboardPasteCommand(
    FileClipboardOperations operations,
    IContentModelService contentModelService,
    IContentModel? ownModel = null,
    Key? key = null)
    : FileClipboardCommand(operations, contentModelService, ownModel)
{
    public override string Title => "Paste from clipboard";
    public override string Description => "Copy or move the files on the system clipboard into the folder this panel shows.";

    public override Key? Key { get; init; } = key ?? new Key(ConsoleKey.V, false, false, true);

    public override bool CanExecute() => Operations.CanPasteInto(TargetModel);

    public override Task ExecuteAsync(object? args) =>
        Control is iConsole.Controls.Control host ? Operations.PasteAsync(TargetModel, host) : Task.CompletedTask;
}

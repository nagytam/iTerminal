using iConsole.Controls;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Commands.Marks;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Controls.MenuBar;

public class EditMenuItem : MenuItem
{
    public EditMenuItem(
        IContentModelService contentModelService,
        CopyDialog copyDialog,
        MoveDialog moveDialog,
        RenameDialog renameDialog,
        DeleteDialog deleteDialog,
        IThemeService themeService,
        MarkPatternDialog? markPatternDialog = null,
        MarkPatternDialog? unmarkPatternDialog = null,
        CompareFoldersDialog? compareFoldersDialog = null,
        FileDiffDialog? fileDiffDialog = null,
        SynchronizeFoldersDialog? synchronizeFoldersDialog = null,
        StripMetadataDialog? stripMetadataDialog = null,
        FileLabelsDialog? fileLabelsDialog = null,
        LabelFilterDialog? labelFilterDialog = null,
        iTerminal.Core.Services.Clipboard.FileClipboardOperations? clipboardOperations = null,
        MultiRenameDialog? multiRenameDialog = null,
        iTerminal.Core.Services.RenameUndoService? renameUndoService = null,
        INotificationService? notificationService = null)
        : base("Edit", null, [], themeService)
    {
        HotKeyChar = 'E';

        if (clipboardOperations is not null)
        {
            // The system clipboard: what another program (Explorer, a mail client, an upload dialog)
            // can paste, and what it copied can be pasted here.
            AddMenuItem(new MenuItem("Copy to Clipboard\tCtrl+C",    new Commands.Clipboard.ClipboardCopyCommand(clipboardOperations, contentModelService), [], themeService));
            AddMenuItem(new MenuItem("Cut to Clipboard\tCtrl+X",     new Commands.Clipboard.ClipboardCutCommand(clipboardOperations, contentModelService), [], themeService));
            AddMenuItem(new MenuItem("Paste from Clipboard\tCtrl+V", new Commands.Clipboard.ClipboardPasteCommand(clipboardOperations, contentModelService), [], themeService));
            AddMenuItem(new MenuItem("-", null, [], themeService));
        }

        AddMenuItem(new MenuItem("Copy Path\tCtrl+Shift+Insert", new iTerminal.Core.Commands.CopyPathCommand(contentModelService), [], themeService));
        AddMenuItem(new MenuItem("Copy Name\tCtrl+Shift+N", new iTerminal.Core.Commands.CopyNameCommand(contentModelService), [], themeService));
        // The panel has its own copy bound to the Menu key; this one is how to find it, and how to reach the menu on a
        // terminal that never sends that key. It acts on the focused panel, or the last one.
        AddMenuItem(new MenuItem("Context Menu\tMenu", new PanelContextMenuCommand(contentModelService), [], themeService));
        AddMenuItem(new MenuItem("Copy Files\tF5",  new CopyDialogCommand(copyDialog, contentModelService), [], themeService));
        AddMenuItem(new MenuItem("Move\tShift+F6",  new MoveDialogCommand(moveDialog, contentModelService), [], themeService));
        AddMenuItem(new MenuItem("Rename\tF6",      new RenameDialogCommand(renameDialog, contentModelService), [], themeService));
        if (multiRenameDialog is not null)
        {
            // No chord: see MultiRenameDialogCommand. The menu is the way in.
            AddMenuItem(new MenuItem("Multi-rename",    new MultiRenameDialogCommand(multiRenameDialog, contentModelService), [], themeService));
        }
        if (renameUndoService is not null)
        {
            // The panel has its own copy bound to Ctrl+Z; this one is how to find it, and how to run it with the mouse.
            AddMenuItem(new MenuItem("Undo rename or move\tCtrl+Z", new UndoRenameCommand(renameUndoService, notificationService, contentModelService), [], themeService));
        }
        AddMenuItem(new MenuItem("-", null, [], themeService));
        AddMenuItem(new MenuItem("Delete\tF8",     new DeleteDialogCommand(deleteDialog, contentModelService), [], themeService));

        // Marking: what F5, Shift+F6 and F8 act on when more than one row should go. Here as much
        // for the keyboards without a numeric keypad as for finding the keys at all.
        AddMenuItem(new MenuItem("-", null, [], themeService));
        AddMenuItem(new MenuItem("Mark\tInsert",           new ToggleMarkCommand(contentModelService), [], themeService));
        if (markPatternDialog is not null)
        {
            AddMenuItem(new MenuItem("Mark by Pattern\tNum+",   new MarkByPatternDialogCommand(markPatternDialog, contentModelService, marking: true), [], themeService));
        }
        if (unmarkPatternDialog is not null)
        {
            AddMenuItem(new MenuItem("Unmark by Pattern\tNum-", new MarkByPatternDialogCommand(unmarkPatternDialog, contentModelService, marking: false), [], themeService));
        }
        AddMenuItem(new MenuItem("Invert Marks\tNum*",     new InvertMarksCommand(contentModelService), [], themeService));
        AddMenuItem(new MenuItem("Mark All",               new MarkAllCommand(contentModelService), [], themeService));
        AddMenuItem(new MenuItem("Unmark All",             new ClearMarksCommand(contentModelService), [], themeService));
        if (compareFoldersDialog is not null)
        {
            // Comparing ends in marks — what each side should copy across — so it lives with them.
            AddMenuItem(new MenuItem("-", null, [], themeService));
            AddMenuItem(new MenuItem("Compare Folders\tShift+F2", new CompareFoldersDialogCommand(compareFoldersDialog, contentModelService), [], themeService));
        }
        if (synchronizeFoldersDialog is not null)
        {
            AddMenuItem(new MenuItem("Synchronise Folders", new SynchronizeFoldersDialogCommand(synchronizeFoldersDialog), [], themeService));
        }
        if (stripMetadataDialog is not null)
        {
            AddMenuItem(new MenuItem("Strip Metadata", new StripMetadataDialogCommand(stripMetadataDialog, contentModelService), [], themeService));
        }
        if (fileLabelsDialog is not null)
        {
            AddMenuItem(new MenuItem("Colour Labels and Tags", new FileLabelsDialogCommand(fileLabelsDialog, contentModelService), [], themeService));
        }
        if (labelFilterDialog is not null)
        {
            AddMenuItem(new MenuItem("Filter by Label", new LabelFilterDialogCommand(labelFilterDialog, contentModelService), [], themeService));
        }
        if (fileDiffDialog is not null)
        {
            AddMenuItem(new MenuItem("Compare Files\tCtrl+F10", new DiffFilesDialogCommand(fileDiffDialog, contentModelService), [], themeService));
        }
    }
}

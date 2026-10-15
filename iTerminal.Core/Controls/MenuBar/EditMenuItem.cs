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
        CompareFoldersDialog? compareFoldersDialog = null)
        : base("Edit", null, [], themeService)
    {
        HotKeyChar = 'E';

        AddMenuItem(new MenuItem("Copy\tF5",        new CopyDialogCommand(copyDialog, contentModelService), [], themeService));
        AddMenuItem(new MenuItem("Move\tShift+F6",  new MoveDialogCommand(moveDialog, contentModelService), [], themeService));
        AddMenuItem(new MenuItem("Rename\tF6",      new RenameDialogCommand(renameDialog, contentModelService), [], themeService));
        AddMenuItem(new MenuItem("-", null, [], themeService));
        AddMenuItem(new MenuItem("Delete\tF8",      new DeleteDialogCommand(deleteDialog, contentModelService), [], themeService));

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
    }
}

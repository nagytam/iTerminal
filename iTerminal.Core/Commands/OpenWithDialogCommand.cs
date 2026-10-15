using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.FileAssociations;

namespace iTerminal.Core.Commands;

/// <summary>
/// Shift+F3 "Open with…" and Ctrl+F4 "Edit with…": the programs for the file under the cursor
/// (<see cref="OpenWithDialog"/>). One class for both, since they differ only in which list they
/// show, the key, and what the row that is always there does.
///
/// <para>Offered for a file on the local disk only: a program is handed a path, and a file inside
/// an archive, a container or on a remote host has none. The panel is read as
/// <c>GetFocusedModel() ?? GetLastContentModel()</c>, so the File menu's entry works too — with a
/// menu open no panel has focus.</para>
/// </summary>
public class OpenWithDialogCommand : ShowDialogCommand<OpenWithDialog>
{
    private readonly IContentModelService _contentModelService;
    private readonly FileAssociationKind _kind;
    private readonly TextFileEditorDialog? _textFileEditorDialog;

    public OpenWithDialogCommand(
        OpenWithDialog dialog,
        IContentModelService contentModelService,
        FileAssociationKind kind,
        TextFileEditorDialog? textFileEditorDialog = null)
        : base(dialog)
    {
        _contentModelService = contentModelService;
        _kind = kind;
        _textFileEditorDialog = textFileEditorDialog;

        // Key ctor is (consoleKey, shift, alt, control).
        Key = kind == FileAssociationKind.Edit
            ? new Key(ConsoleKey.F4, false, false, true)
            : new Key(ConsoleKey.F3, true, false, false);
    }

    public override string Title => _kind == FileAssociationKind.Edit ? "Edit with…" : "Open with…";

    public override string Description => _kind == FileAssociationKind.Edit
        ? "Choose the editor for the selected file, or set the one F4 uses for files like it."
        : "Choose the program to open the selected file with, or set the one Enter uses for files like it.";

    public override Key? Key { get; init; }

    private Url? FocusedUrl =>
        (_contentModelService?.GetFocusedModel() ?? _contentModelService?.GetLastContentModel())?.GetFocusedEntry()?.Url;

    public override bool CanExecute() => FileAssociationRunner.LocalFilePath(FocusedUrl) is not null;

    protected override void OnBeforeShow(OpenWithDialog dialog)
    {
        var url = FocusedUrl;
        var path = FileAssociationRunner.LocalFilePath(url) ?? string.Empty;

        Func<Task>? builtInEditor = null;
        if (_kind == FileAssociationKind.Edit
            && _textFileEditorDialog is { } editor
            && url is not null
            && EditTextFileDialogCommand.SupportedExtensions.Contains(url.Extension.TrimStart('.')))
        {
            builtInEditor = () =>
            {
                editor.Url = url;
                return Control?.ShowDialogAsync(editor) ?? Task.CompletedTask;
            };
        }

        dialog.Load(_kind, path, builtInEditor);
    }
}

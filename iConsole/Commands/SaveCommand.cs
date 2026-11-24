using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;

namespace iConsole.Commands;

public class SaveCommand(
    ITextModel textModel,
    IContentModelService contentModelService)
    : Command
{
    private readonly ITextModel _textModel = textModel;
    private readonly IContentModelService _contentModelService = contentModelService;
    public override string Title => iConsole.Localization.UiText.Get("Key.Save", "F2-Save");
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F2, false, false, false);

    public override bool CanExecute()
    {
        var canExecute = base.CanExecute();
        if (Control?.Parent?.Parent is TextFileEditorDialog textFileEditorDialog)
        {
            var textEditorControl = textFileEditorDialog.TextEditorControl;
            canExecute = canExecute && textEditorControl.IsDirty;
        }
        return canExecute;
    }

        public override async Task ExecuteAsync(object? args)
        {
            // Find the nearest TextFileEditorDialog in the parent chain. Tests create the
            // TextEditorControl and set its Parent to the dialog directly, so checking
            // Parent.Parent is brittle. Walk the ancestor chain instead.
            var parent = Control?.Parent;
            TextFileEditorDialog? textFileEditorDialog = null;
            while (parent is not null)
            {
                if (parent is TextFileEditorDialog tfed)
                {
                    textFileEditorDialog = tfed;
                    break;
                }
                parent = parent.Parent;
            }

            if (textFileEditorDialog is not null)
            {
                await WriteAsync(textFileEditorDialog, _textModel, _contentModelService);
            }
        }

    /// <summary>
    /// Writes the editor's text to its file, with the line break and the code page the file was read with, and clears the
    /// changed mark. In the hex view the bytes are written as they are shown. False when there is nowhere to write (no file,
    /// no panel). Shared by F2 and by the question asked when an edited file is closed.
    /// </summary>
    internal static async Task<bool> WriteAsync(TextFileEditorDialog dialog, ITextModel textModel, IContentModelService contentModelService)
    {
        var url = dialog.Url;
        var contentModel = contentModelService.GetLastContentModel();
        if (contentModel is null || url is null)
        {
            return false;
        }

        if (dialog.IsHexMode)
        {
            byte[] bytes = dialog.HexEditorControl.Data;
            using var bytesStream = new MemoryStream(bytes, writable: false);
            await contentModel.WriteFileAsync(url, bytesStream);
            dialog.NoteSaved(bytes);
            dialog.TextEditorControl.IsDirty = false;
            dialog.TextEditorControl.Refresh();
            return true;
        }

        // The break the file came with, not the system's: saving must not change every line of an LF file on Windows.
        var content = string.Join(textModel.LineEnding, textModel.Lines);
        using var stream = content.ToMemoryStream(textModel.Encoding);
        await contentModel.WriteFileAsync(url, stream);
        dialog.NoteSaved(null);
        dialog.TextEditorControl.IsDirty = false;
        dialog.TextEditorControl.Refresh();
        return true;
    }
}

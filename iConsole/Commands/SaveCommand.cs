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
    public override string Title => "F2-Save";
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

        public override async Task ExecuteAsync(object? arg)
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
                var textEditorControl = textFileEditorDialog.TextEditorControl;
                var content = string.Join(Environment.NewLine, _textModel.Lines);
                var url = textFileEditorDialog.Url;
                using var stream = content.ToMemoryStream(_textModel.Encoding);
                var contentModel = _contentModelService.GetLastContentModel();
                if (contentModel is not null && url is not null)
                {
                    await contentModel.WriteFileAsync(url, stream);
                    textEditorControl.IsDirty = false;
                    textEditorControl.Refresh();
                }
            }
        }
}

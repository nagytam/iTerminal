using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.Validations;

namespace iTerminal.Core.Commands;

public class CreateFileCommand(
    NewFileValidationModel newFileValidator,
    IContentModelService contentModelService
    ) 
    : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute()
    {
        if (Control?.Parent is CreateFileDialog createFileDialog)
        {
            return newFileValidator.EnsureCurrent();
        }
        return base.CanExecute();
    }

    public override async Task ExecuteAsync(object? arg)
    {
        if (Control?.Parent is CreateFileDialog createFileDialog)
        {
            var newFileUrl = createFileDialog.BaseUrl.Combine(createFileDialog.NewFileName);
            var model = contentModelService.GetLastContentModel();

            // The last line of defence against truncating a file: writing the new, empty file over
            // one that exists replaces its contents. The validation normally stops this first, and when
            // it did not, the dialog says so rather than quietly staying open.
            if (newFileUrl.ContainerType == ContainerType.FileSystem && File.Exists(newFileUrl.FullName))
            {
                createFileDialog.ReportFailure(UrlValidationModel.ExistsMessage);
                return;
            }

            if (model is null)
            {
                createFileDialog.ReportFailure("There is no folder to create the file in.");
                return;
            }

            try
            {
                await model.WriteFileAsync(newFileUrl, new MemoryStream());
                model.Update();
            }
            catch (Exception exception)
            {
                createFileDialog.ReportFailure($"Could not create the file: {exception.Message}");
                return;
            }

            createFileDialog.Close();
            createFileDialog.Reset();
        }
    }
}

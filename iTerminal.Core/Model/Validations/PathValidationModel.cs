using iConsole.Data;
using iConsole.Model;
using iConsole.Model.Validations;
using iConsole.Services;

namespace iTerminal.Core.Model.Validations;

public class PathValidationModel(ITextModel model, IDateTimeProviderService dateTimeProviderService)
    : ValidationModel<ITextModel>(dateTimeProviderService, model)
{
    public override void Validate(DateTime now)
    {
        if (Model is not null)
        {
            var inputPath = Model.Lines[0];
            if (string.IsNullOrEmpty(inputPath))
            {
                SetValidationResult(now, false, "Path cannot be empty");
                return;
            }

            var url = new Url(inputPath);
            if (url is not null && !url.IsValid(PathExistMode.Default))
            {
                SetValidationResult(now, false, "Invalid path");
            }
            else
            {
                SetValidationResult(now, true, string.Empty);
            }
        }
        else
        {
            SetValidationResult(now, false, "No model");
        }
    }
}

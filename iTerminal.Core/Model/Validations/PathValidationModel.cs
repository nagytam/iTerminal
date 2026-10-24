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
            // Empty is invalid but says nothing, as on the create dialogs: a red message before the first key is noise.
            if (string.IsNullOrEmpty(inputPath))
            {
                SetValidationResult(now, false, string.Empty);
                return;
            }

            // A path the file system cannot represent (a NUL character) is refused, not thrown from the key handler.
            bool accepted;
            try
            {
                accepted = new Url(inputPath).IsValid(PathExistMode.Default);
            }
            catch (ArgumentException)
            {
                accepted = false;
            }

            SetValidationResult(now, accepted, accepted ? string.Empty : UrlValidationModel.InvalidMessage);
        }
        else
        {
            SetValidationResult(now, false, "No model");
        }
    }
}

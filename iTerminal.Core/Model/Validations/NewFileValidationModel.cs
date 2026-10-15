using iConsole.Data;
using iConsole.Model;
using iConsole.Services;

namespace iTerminal.Core.Model.Validations;

/// <summary>Validates a new-file path. See <see cref="UrlValidationModel"/>.</summary>
public class NewFileValidationModel(ITextModel model, IDateTimeProviderService dateTimeProviderService)
    : UrlValidationModel(model, dateTimeProviderService, PathExistMode.NewFile)
{
    protected override bool ExistsAlready(Url url) => File.Exists(url.FullName);
}

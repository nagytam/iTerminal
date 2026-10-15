using iConsole.Data;
using iConsole.Model;
using iConsole.Services;

namespace iTerminal.Core.Model.Validations;

/// <summary>Validates a new-folder path. See <see cref="UrlValidationModel"/>.</summary>
public class FolderValidationModel(ITextModel textModel, IDateTimeProviderService dateTimeProviderService)
    : UrlValidationModel(textModel, dateTimeProviderService, PathExistMode.NewFolder)
{
    protected override bool ExistsAlready(Url url) => Directory.Exists(url.FullName);
}

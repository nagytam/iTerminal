using iConsole.Data;
using iConsole.Model;
using iConsole.Services;

namespace iTerminal.Core.Model.Validations;

/// <summary>Validates a target path that may be an existing folder or a new file. See <see cref="UrlValidationModel"/>.</summary>
public class FolderOrNewFilesValidationModel(ITextModel textModel, IDateTimeProviderService dateTimeProviderService)
    : UrlValidationModel(textModel, dateTimeProviderService, PathExistMode.FolderOrNewFile)
{
    // The only refusal in this mode is a file that is already there, so that is what "already exists" names.
    protected override bool ExistsAlready(Url url) => File.Exists(url.FullName);
}

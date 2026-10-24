using iConsole.Data;
using iConsole.Model;
using iConsole.Services;

namespace iTerminal.Core.Model.Validations;

/// <summary>Validates a new-folder path. See <see cref="UrlValidationModel"/>.</summary>
public class FolderValidationModel(ITextModel textModel, IDateTimeProviderService dateTimeProviderService)
    : UrlValidationModel(textModel, dateTimeProviderService, PathExistMode.NewFolder)
{
    protected override bool ExistsAlready(Url url) => Directory.Exists(url.FullName);

    /// <summary>
    /// A name typed into a create dialog is inside the panel's folder however it is written, as the create command
    /// writes it: "sub/new" is checked and created in the panel's folder, not in the working directory.
    /// </summary>
    protected override Url Resolve(string rawInput) =>
        BaseUrl is { ContainerType: ContainerType.FileSystem, PathType: PathType.Folder } baseUrl && !Path.IsPathRooted(rawInput)
            ? baseUrl.Combine(rawInput)
            : base.Resolve(rawInput);
}

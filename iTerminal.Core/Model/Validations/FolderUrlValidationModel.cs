using iConsole.Data;
using iConsole.Model;
using iConsole.Model.Validations;
using iConsole.Services;

namespace iTerminal.Core.Model.Validations;

/// <summary>
/// Validates what has been typed into the folder selector's Path box. Anything carrying a scheme the
/// app knows is accepted on sight — a remote host, a device or a music drive cannot be checked
/// without doing the very work the navigation is about to do, and a validator that blocks on the
/// network has no business running off a keystroke. Everything else is a local path, and that one
/// *can* be checked cheaply: <see cref="Url"/> caches the directory probe for five seconds.
/// </summary>
public class FolderUrlValidationModel(ITextModel model, IDateTimeProviderService dateTimeProviderService)
    : ValidationModel<ITextModel>(dateTimeProviderService, model)
{
    /// <summary>
    /// The schemes the Path box accepts, in the order the hint line lists them. This is the same set
    /// <see cref="Container"/> declares and <c>ContentModelService.AddContentModel</c> switches on;
    /// a scheme added to the app but not here is one the user is told does not exist.
    /// </summary>
    public static readonly string[] SupportedSchemes =
    [
        Container.FILE,
        Container.ZIP_ARCHIVE,
        Container.GZ_ARCHIVE,
        Container.TAR_GZ_ARCHIVE,
        Container.SSH,
        Container.FTP,
        Container.SMB,
        Container.IPHONE,
        Container.MEMORY_DRIVE,
        Container.MUSIC_CATALOG,
        Container.MUSIC_PLAYLIST,
        Container.DOCKER,
        Container.GIT,
        Container.SEARCH_RESULTS,
        Container.PREVIEW,
        Container.ARCHIVE,
        Container.RECYCLE,
    ];

    /// <summary>True when <paramref name="path"/> names one of <see cref="SupportedSchemes"/>.</summary>
    public static bool HasSupportedScheme(string path) =>
        SupportedSchemes.Any(scheme => path.StartsWith(scheme, StringComparison.OrdinalIgnoreCase));

    public override void Validate(DateTime now)
    {
        if (Model is null)
        {
            SetValidationResult(now, true, string.Empty);
            return;
        }

        var path = Model.Lines[0]?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(path))
        {
            SetValidationResult(now, false, "Type a path or a URL!");
            return;
        }

        if (HasSupportedScheme(path))
        {
            SetValidationResult(now, true, string.Empty);
            return;
        }

        if (path.Contains("://"))
        {
            SetValidationResult(now, false, "Unknown scheme — see the list below.");
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                SetValidationResult(now, true, string.Empty);
            }
            else
            {
                SetValidationResult(now, false, "No such folder.");
            }
        }
        catch (Exception exception)
        {
            SetValidationResult(now, false, exception.Message);
        }
    }
}

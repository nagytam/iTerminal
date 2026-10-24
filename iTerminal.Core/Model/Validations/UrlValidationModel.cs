using iConsole.Data;
using iConsole.Model;
using iConsole.Model.Validations;
using iConsole.Services;

namespace iTerminal.Core.Model.Validations;

/// <summary>
/// Shared validation flow for path-input textboxes: requires a non-empty path and checks it with
/// <see cref="Url.IsValid"/> under the configured <see cref="PathExistMode"/>. Subclasses pick the
/// default mode and may refine the failure message via <see cref="ExistsAlready"/> when the path
/// is invalid only because it already exists.
/// </summary>
public abstract class UrlValidationModel(ITextModel textModel, IDateTimeProviderService dateTimeProviderService, PathExistMode defaultPathExistMode)
    : ValidationModel<ITextModel>(dateTimeProviderService, textModel)
{
    public PathExistMode PathExistMode { get; set; } = defaultPathExistMode;

    /// <summary>
    /// The folder a bare name is created in. The create dialogs hold a name, not a path, and without
    /// this the name was checked against the process's working directory — so "already exists"
    /// was never reported for a file sitting right there in the panel, and creating it truncated it.
    /// Only a local folder is used; a remote drive's names cannot be checked from here.
    /// </summary>
    public Url? BaseUrl { get; set; }

    /// <summary>Whether the path the input names is invalid only because something is already there.</summary>
    protected virtual bool ExistsAlready(Url url) => false;

    /// <summary>The URL the input names: a bare name inside <see cref="BaseUrl"/>, anything else as typed.</summary>
    protected virtual Url Resolve(string rawInput)
    {
        var baseUrl = BaseUrl;
        if (baseUrl is not null
            && baseUrl.ContainerType == ContainerType.FileSystem
            && baseUrl.PathType == PathType.Folder
            && string.Equals(Path.GetFileName(rawInput), rawInput, StringComparison.Ordinal))
        {
            return baseUrl.Combine(rawInput);
        }

        return new Url(rawInput);
    }

    /// <summary>Shown when the name is taken. The dialog's error line adds the "Error: " itself.</summary>
    public const string ExistsMessage = "A file or folder with this name already exists.";

    /// <summary>Shown for a name or path the file system would refuse.</summary>
    public const string InvalidMessage = "Not a valid name or path.";

    /// <summary>
    /// An empty field is invalid — the create and copy buttons stay disabled — but says nothing.
    /// It used to say "Supply path!" in red the moment a dialog opened, before the user had typed a
    /// key, and the other two messages carried an "Error: " of their own, which the dialog's error
    /// line prefixes again: "Error: Error: Path exists already!".
    /// </summary>
    public override void Validate(DateTime now)
    {
        if (Model is not null)
        {
            var rawInput = Model.Lines[0];
            if (string.IsNullOrEmpty(rawInput))
            {
                SetValidationResult(now, false, string.Empty);
                return;
            }
            var (accepted, taken) = Judge(rawInput);
            if (!accepted)
            {
                SetValidationResult(now, false, taken ? ExistsMessage : InvalidMessage);
                return;
            }
        }
        SetValidationResult(now, true, string.Empty);
    }

    /// <summary>
    /// Whether the input names a place the file system accepts, and whether that place is taken already. A name the
    /// file system cannot even represent — a NUL character, pasted into the box — is refused, not thrown: the throw
    /// came out of the key handler and left the dialog with no message at all.
    /// </summary>
    private (bool Accepted, bool Taken) Judge(string rawInput)
    {
        try
        {
            var url = Resolve(rawInput);
            return url.IsValid(PathExistMode) ? (true, false) : (false, ExistsAlready(url));
        }
        catch (ArgumentException)
        {
            return (false, false);
        }
    }
}

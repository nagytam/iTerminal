using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands.Drives;

/// <summary>
/// Points the focused panel at one of the tool-backed drives — or, pressed again on that drive,
/// takes it back to the folder it came from. The toggle is what makes a hot key for a drive usable:
/// without it the way back from <c>docker://</c> is the folder selector.
///
/// <para>Global, on a Ctrl+function key, for the reason set out under "Choosing a global hot key":
/// the focused panel claims every Alt+printable chord for its search filter before the status bar
/// sees it.</para>
/// </summary>
public abstract class OpenDriveCommand(IContentModelService contentModelService) : Command
{
    protected IContentModelService ContentModelService => contentModelService;

    /// <summary>The drive this command opens.</summary>
    protected abstract ContainerType DriveType { get; }

    /// <summary>Where on the drive to open the panel, given where it is now.</summary>
    protected abstract Url Destination(IContentModel panel);

    public override async Task ExecuteAsync(object? args)
    {
        // The last-focused panel when none has focus: a menu that is open holds the keyboard, and
        // these commands are on the Navigate menu too.
        var panel = contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();
        if (panel is null)
        {
            return;
        }

        if (panel.Url.ContainerType == DriveType && panel is ContentModelAsyncDrive drive)
        {
            await contentModelService.SetUrlAsync(panel.Name, drive.ReturnUrl);
            return;
        }

        await contentModelService.SetUrlAsync(panel.Name, Destination(panel));
    }
}

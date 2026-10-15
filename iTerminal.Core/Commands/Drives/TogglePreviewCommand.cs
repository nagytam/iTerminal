using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands.Drives;

/// <summary>
/// Ctrl+F3: quick view. Turns the panel the user is <i>not</i> in into a preview of the file under
/// the cursor — so the arrows keep moving through the listing and the preview follows — and,
/// pressed again from either panel, gives that panel back the folder it was showing. F3 views a
/// file; Ctrl+F3 is the glance that does not leave the listing.
/// </summary>
public class TogglePreviewCommand(IContentModelService contentModelService) : Command
{
    public override string Title => "Quick view";
    public override string Description => "Preview the file under the cursor in the other panel; press again to close the preview.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } = new Key(ConsoleKey.F3, false, false, true);

    public override async Task ExecuteAsync(object? args)
    {
        var models = contentModelService.ContentModels.AsSnapshot();
        if (models.OfType<ContentModelPreview>().FirstOrDefault() is { } preview)
        {
            await contentModelService.SetUrlAsync(preview.Name, preview.ReturnUrl);
            return;
        }

        // The last-focused panel when none has focus — run from the File menu, the menu has it.
        var focused = contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();
        var other = focused is null ? null : models.FirstOrDefault(m => m.Name != focused.Name);
        if (other is null)
        {
            return;
        }

        await contentModelService.SetUrlAsync(other.Name, new Url(Container.PREVIEW));
    }
}

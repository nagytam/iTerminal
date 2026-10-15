using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands.Drives;

/// <summary>Ctrl+F8 — beside F8, which is what fills it: the recycle bin as a drive. See <see cref="OpenDriveCommand"/>.</summary>
public class RecycleDriveCommand(IContentModelService contentModelService) : OpenDriveCommand(contentModelService)
{
    public override string Title => "Recycle bin";
    public override string Description => "Browse deleted files in the focused panel, to restore them or delete them for good; press again to go back.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } = new Key(ConsoleKey.F8, false, false, true);

    protected override ContainerType DriveType => ContainerType.Recycle;

    protected override Url Destination(IContentModel panel) => new(Container.RECYCLE);
}

/// <summary>
/// Enter on the <c>recycle://</c> drive: puts the marked rows, or the row under the cursor, back
/// where they were deleted from. Attached to every content browser and dormant everywhere else —
/// the focused model has to be a recycle panel, and on its <c>..</c> row Enter is left to mean
/// "leave the drive".
///
/// <para>It reads the focused model and nothing of its own, so both panels' instances answer alike:
/// the focused panel is offered the key first and takes it, and when it declines, the other
/// declines for the same reason.</para>
/// </summary>
public class RestoreRecycledCommand(IContentModelService contentModelService) : Command
{
    public override string Title => "Restore";
    public override string Description => "Put the selected deleted files back where they were deleted from.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    private ContentModelRecycleDrive? Panel => contentModelService?.GetFocusedModel() as ContentModelRecycleDrive;

    public override bool CanExecute() => Panel?.RestoreTargets().Count > 0;

    public override Task ExecuteAsync(object? args)
    {
        if (Panel is { } panel)
        {
            panel.Restore(panel.RestoreTargets());
        }

        return Task.CompletedTask;
    }
}

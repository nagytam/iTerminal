using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Commands.Drives;

/// <summary>Ctrl+F9: the containers on this machine as a drive. See <see cref="OpenDriveCommand"/>.</summary>
public class DockerDriveCommand(IContentModelService contentModelService) : OpenDriveCommand(contentModelService)
{
    public override string Title => "Docker containers";
    public override string Description => "Browse Docker containers' files in the focused panel; press again to go back.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } = new Key(ConsoleKey.F9, false, false, true);

    protected override ContainerType DriveType => ContainerType.Docker;

    protected override Url Destination(IContentModel panel) => new(Container.DOCKER);
}

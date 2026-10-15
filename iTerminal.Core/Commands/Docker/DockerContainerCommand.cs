using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Docker;

namespace iTerminal.Core.Commands.Docker;

/// <summary>
/// Base for the commands that operate the container under the cursor on the <c>docker://</c> root.
/// Attached to every content browser and dormant everywhere else: the focused model has to be a
/// docker panel <em>and</em> this browser's own (a key the focused panel declined is offered to
/// every control, so without the identity check the other panel would act on it), and the row under
/// the cursor has to be a container.
/// </summary>
public abstract class DockerContainerCommand(IContentModel contentModel, IContentModelService contentModelService) : Command
{
    protected (ContentModelDockerDrive Model, DockerContainerEntry Container)? Target
    {
        get
        {
            var focused = contentModelService.GetFocusedModel();
            return focused is ContentModelDockerDrive docker
                   && ReferenceEquals(focused, contentModel)
                   && docker.GetFocusedEntry() is DockerContainerEntry container
                ? (docker, container)
                : null;
        }
    }

    public override bool CanExecute() => Target is not null;
}
